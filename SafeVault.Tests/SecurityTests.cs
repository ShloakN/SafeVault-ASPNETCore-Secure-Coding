using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SafeVault.Api.Models;
using Xunit;

namespace SafeVault.Tests;

public sealed class SafeVaultTestApplication : WebApplicationFactory<Program>
{
    public const string SigningKey = "test-only-signing-key-0123456789-ABCDEFG";

    private readonly string _connectionString =
        $"Data Source=SafeVaultTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _anchorConnection;

    public SafeVaultTestApplication()
    {
        _anchorConnection = new SqliteConnection(_connectionString);
        _anchorConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", SigningKey);
        builder.UseSetting("Jwt:Issuer", "SafeVault");
        builder.UseSetting("Jwt:Audience", "SafeVaultClients");
        builder.UseSetting("ConnectionStrings:SafeVault", _connectionString);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _anchorConnection.Dispose();
    }
}

public class SecurityTests : IClassFixture<SafeVaultTestApplication>
{
    private const string ValidPassword = "StrongPassword123!";
    private readonly SafeVaultTestApplication _factory;

    public SecurityTests(SafeVaultTestApplication factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProtectedEndpointsRequireAuthentication()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/users")).StatusCode);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("expired")]
    [InlineData("wrong-signature")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    public async Task ProtectedEndpointRejectsInvalidJwt(string tokenKind)
    {
        using var client = _factory.CreateClient();
        var token = tokenKind switch
        {
            "expired" => CreateToken(expires: DateTime.UtcNow.AddMinutes(-10)),
            "wrong-signature" => CreateToken(signingKey: "incorrect-test-signing-key-0123456789"),
            "wrong-issuer" => CreateToken(issuer: "OtherIssuer"),
            "wrong-audience" => CreateToken(audience: "OtherAudience"),
            _ => "not-a-token"
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/notes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email", ValidPassword)]
    [InlineData("user@example.com", "weak")]
    [InlineData("", ValidPassword)]
    [InlineData("user@example.com", "NoDigitOrSymbol")]
    public async Task RegistrationRejectsInvalidEmailOrWeakPassword(string email, string password)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/register", "{}")]
    [InlineData("/api/auth/register", "{\"email\":\"user@example.com\"}")]
    [InlineData("/api/auth/login", "{}")]
    [InlineData("/api/auth/login", "{\"password\":\"StrongPassword123!\"}")]
    public async Task AuthenticationEndpointsRejectMissingFields(string endpoint, string body)
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(endpoint, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/login")]
    public async Task AuthenticationEndpointsRejectMalformedJson(string endpoint)
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(endpoint, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginTreatsSqlInjectionShapedEmailAsData()
    {
        using var client = _factory.CreateClient();
        var email = $"user{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"{email}' OR '1'='1' --",
            password = ValidPassword
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, email)).StatusCode);
    }

    [Fact]
    public async Task PublicRegistrationCannotGrantAdminRole()
    {
        using var client = _factory.CreateClient();
        var email = $"user{Guid.NewGuid():N}@example.com";
        var registration = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = ValidPassword, role = "Admin" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var token = await LoginAndGetTokenAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminRoleCanAccessAdminEndpoint()
    {
        using var client = _factory.CreateClient();
        var email = $"admin{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.NotNull(user);
            var result = await users.AddToRoleAsync(user!, "Admin");
            Assert.True(result.Succeeded);
        }

        var token = await LoginAndGetTokenAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NoteContentIsReturnedAsJsonData()
    {
        using var client = _factory.CreateClient();
        var email = $"xss{Guid.NewGuid():N}@example.com";
        var token = await RegisterAndLoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        const string payload = "<script>alert('xss')</script>";

        var response = await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest(payload));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var note = await response.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(note);
        Assert.Equal(payload, note.Content);
    }

    [Fact]
    public async Task NotesRejectOversizedContent()
    {
        using var client = _factory.CreateClient();
        var token = await RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/notes",
            new CreateNoteRequest(new string('x', 2001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NotesRejectMissingContent()
    {
        using var client = _factory.CreateClient();
        var token = await RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/notes", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UsersCanOnlyReadTheirOwnNotes()
    {
        using var ownerClient = _factory.CreateClient();
        using var otherClient = _factory.CreateClient();
        var ownerToken = await RegisterAndLoginAsync(ownerClient);
        var otherToken = await RegisterAndLoginAsync(otherClient);
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        Assert.Equal(HttpStatusCode.Created,
            (await ownerClient.PostAsJsonAsync("/api/notes", new CreateNoteRequest("owner secret"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await otherClient.PostAsJsonAsync("/api/notes", new CreateNoteRequest("other secret"))).StatusCode);

        var ownerNotesResponse = await ownerClient.GetAsync("/api/notes");
        var otherNotesResponse = await otherClient.GetAsync("/api/notes");
        var ownerNotes = await ownerNotesResponse.Content.ReadFromJsonAsync<List<NoteResponse>>();
        var otherNotes = await otherNotesResponse.Content.ReadFromJsonAsync<List<NoteResponse>>();

        Assert.Equal(HttpStatusCode.OK, ownerNotesResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherNotesResponse.StatusCode);
        Assert.Equal(new[] { "owner secret" }, ownerNotes!.Select(note => note.Content));
        Assert.Equal(new[] { "other secret" }, otherNotes!.Select(note => note.Content));
    }

    [Fact]
    public async Task IdentityStoresAHashInsteadOfThePlaintextPassword()
    {
        using var client = _factory.CreateClient();
        var email = $"password{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(email);

        Assert.NotNull(user);
        Assert.NotNull(user!.PasswordHash);
        Assert.NotEqual(ValidPassword, user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            users.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, ValidPassword));
    }

    private static async Task RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = ValidPassword });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<string> RegisterAndLoginAsync(HttpClient client, string? email = null)
    {
        email ??= $"user{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);
        return await LoginAndGetTokenAsync(client, email);
    }

    private static async Task<string> LoginAndGetTokenAsync(HttpClient client, string email)
    {
        var response = await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(result);
        return result!.AccessToken;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password = ValidPassword });

    private static string CreateToken(
        string issuer = "SafeVault",
        string audience = "SafeVaultClients",
        string signingKey = SafeVaultTestApplication.SigningKey,
        DateTime? expires = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = expires ?? DateTime.UtcNow.AddMinutes(10);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            new[] { new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) },
            notBefore: expiry <= DateTime.UtcNow ? expiry.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(-1),
            expires: expiry,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn);
}
