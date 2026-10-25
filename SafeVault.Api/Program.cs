using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SafeVault.Api.Data;
using SafeVault.Api.Models;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("SafeVault")
    ?? "Data Source=safevault.db";
builder.Services.AddDbContext<SafeVaultDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddIdentityCore<IdentityUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<SafeVaultDbContext>()
.AddSignInManager();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Configure Jwt:Key using user-secrets or environment variables.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "SafeVault",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "SafeVaultClients",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SafeVaultDbContext>();
    await db.Database.EnsureCreatedAsync();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "User", "Admin" })
        if (!await roles.RoleExistsAsync(role))
            await roles.CreateAsync(new IdentityRole(role));
}

app.MapPost("/api/auth/register", async (RegisterRequest request,
    UserManager<IdentityUser> users) =>
{
    if (!ValidationHelper.ValidEmail(request.Email) ||
        !ValidationHelper.ValidPasswordShape(request.Password))
        return Results.BadRequest(new { error = "Provide a valid email and a password of at least 12 characters including upper, lower, digit, and symbol." });

    var user = new IdentityUser { UserName = request.Email.Trim(), Email = request.Email.Trim() };
    var result = await users.CreateAsync(user, request.Password);
    if (!result.Succeeded)
        return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });

    // Public registration always assigns the least-privileged role.
    await users.AddToRoleAsync(user, "User");
    return Results.Created($"/api/users/{user.Id}", new { message = "Registration successful" });
}).AllowAnonymous();

app.MapPost("/api/auth/login", async (LoginRequest request,
    UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn,
    IConfiguration config) =>
{
    var user = await users.FindByEmailAsync(request.Email.Trim());
    if (user is null || !await signIn.CanSignInAsync(user) ||
        !await users.CheckPasswordAsync(user, request.Password))
        return Results.Unauthorized();

    var roles = await users.GetRolesAsync(user);
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Id),
        new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };
    claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var issuer = config["Jwt:Issuer"] ?? "SafeVault";
    var audience = config["Jwt:Audience"] ?? "SafeVaultClients";
    var token = new JwtSecurityToken(issuer, audience, claims,
        expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: credentials);
    return Results.Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), tokenType = "Bearer", expiresIn = 1800 });
}).AllowAnonymous();

app.MapGet("/api/notes", [Authorize] async (ClaimsPrincipal principal, SafeVaultDbContext db) =>
{
    var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
    var notes = await db.Notes.AsNoTracking().Where(n => n.OwnerId == userId)
        .OrderBy(n => n.Id).Select(n => new NoteResponse(n.Id, n.Content)).ToListAsync();
    return Results.Ok(notes);
});

app.MapPost("/api/notes", [Authorize] async (CreateNoteRequest request,
    ClaimsPrincipal principal, SafeVaultDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 2000)
        return Results.BadRequest(new { error = "Content must contain 1–2000 characters." });
    var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

    var note = new Note { OwnerId = userId, Content = request.Content.Trim() };
    db.Notes.Add(note);
    await db.SaveChangesAsync();
    // Return user input as JSON data. A client must render it as text, not HTML.
    return Results.Created($"/api/notes/{note.Id}", new NoteResponse(note.Id, note.Content));
});

app.MapGet("/api/admin/users", [Authorize(Roles = "Admin")] async (UserManager<IdentityUser> users) =>
{
    var allUsers = await users.Users.AsNoTracking()
        .Select(u => new { u.Id, u.Email, u.UserName }).ToListAsync();
    return Results.Ok(allUsers);
});

app.MapGet("/", () => Results.Ok(new { application = "SafeVault API", status = "running" }));
app.Run();

public partial class Program { }

static class ValidationHelper
{
    public static bool ValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) &&
        email.Length <= 254 &&
        new EmailAddressAttribute().IsValid(email.Trim());

    public static bool ValidPasswordShape(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= 12 && password.Length <= 128 &&
        password.Any(char.IsUpper) && password.Any(char.IsLower) &&
        password.Any(char.IsDigit) && password.Any(c => !char.IsLetterOrDigit(c));
}
