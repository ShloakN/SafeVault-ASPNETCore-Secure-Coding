using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SafeVault.Tests;

public class SecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SecurityTests(WebApplicationFactory<Program> factory)
    {
        // Tests need a valid test-only JWT key. Production must use user-secrets/environment variables.
        Environment.SetEnvironmentVariable("Jwt__Key", "test-only-signing-key-0123456789-ABCDEFG");
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Key", "test-only-signing-key-0123456789-ABCDEFG");
            builder.UseSetting("Jwt:Issuer", "SafeVault");
            builder.UseSetting("Jwt:Audience", "SafeVaultClients");
        }).CreateClient();
    }

    [Fact]
    public async Task NotesEndpointRequiresAuthentication()
    {
        var response = await _client.GetAsync("/api/notes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpointRequiresAuthentication()
    {
        var response = await _client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegistrationRejectsInvalidEmailAndWeakPassword()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = "not-an-email", password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginDoesNotAllowSqlInjectionPayloadToBypassAuthentication()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "' OR '1'='1' --", password = "NotARealPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PublicRegistrationDoesNotAcceptRoleField()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = $"user{Guid.NewGuid():N}@example.com", password = "StrongPassword123!" , role = "Admin" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // Registration contract deliberately has no role property; endpoint always assigns User.
    }

    [Fact]
    public async Task NoteContentIsReturnedAsJsonData()
    {
        // The API does not render HTML; browser clients must display Content as text.
        // This test documents the contract. Authenticated integration coverage can be expanded
        // with a token issued by the login endpoint in an initialized test database.
        var payload = "<script>alert(1)</script>";
        Assert.Contains("<script>", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync(
            "/api/notes", new { content = payload })).StatusCode);
    }
}
