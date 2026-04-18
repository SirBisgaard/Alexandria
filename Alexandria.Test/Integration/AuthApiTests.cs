using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.Tests.Integration;

[TestClass]
public sealed class AuthApiTests : IntegrationTestBase
{
    // ---------- POST /api/alexandria/auth/login ----------

    [TestMethod]
    public async Task Login_ValidCredentials_Returns200WithToken()
    {
        var response = await Client.PostAsJsonAsync("/api/alexandria/auth/login", new { username = "alexander", password = "TheGreat" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.IsNotNull(body);
        Assert.IsNotEmpty(body.Token);
        Assert.AreEqual("Alexander The Great", body.DisplayName);
        Assert.AreNotEqual(Guid.Empty, body.UserUuid);
    }

    [TestMethod]
    public async Task Login_UnknownUsername_Returns401()
    {
        var response = await Client.PostAsJsonAsync("/api/alexandria/auth/login", new { username = "ghost", password = "whatever" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_WrongPassword_Returns401()
    {
        var response = await Client.PostAsJsonAsync("/api/alexandria/auth/login", new { username = "alexander", password = "NotThePassword" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_UpdatesLastLoginTimestamp()
    {
        var userRepo = GetService<IUserRepository>();
        var before = (await userRepo.GetByUsername("alexander"))!.LastLoginAt;

        // Force wall-clock to advance past the repository's 1-tick resolution.
        await Task.Delay(10);
        await LoginAsync();

        var after = (await userRepo.GetByUsername("alexander"))!.LastLoginAt;
        Assert.IsGreaterThan(before, after);
    }

    [TestMethod]
    public async Task Login_AllowAnonymous_WorksWithoutAuthHeader()
    {
        // The default Client has no Authorization header; login still succeeds.
        Assert.IsNull(Client.DefaultRequestHeaders.Authorization);
        var response = await Client.PostAsJsonAsync("/api/alexandria/auth/login", new { username = "alexander", password = "TheGreat" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- GET /api/alexandria/auth/me ----------

    [TestMethod]
    public async Task Me_WithValidToken_Returns200WithUserClaims()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/auth/me");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.IsNotNull(body);
        Assert.AreEqual("alexander", body.Username);
        Assert.AreEqual("Alexander The Great", body.DisplayName);
    }

    [TestMethod]
    public async Task Me_WithMissingToken_Returns401()
    {
        var response = await Client.GetAsync("/api/alexandria/auth/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Me_WithMalformedToken_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-token");

        var response = await Client.GetAsync("/api/alexandria/auth/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Me_WithExpiredToken_Returns401()
    {
        var userRepo = GetService<IUserRepository>();
        var alexander = (await userRepo.GetByUsername("alexander"))!;
        var expired = MintTokenForUser(alexander.UserUuid, ttl: TimeSpan.FromSeconds(-60));

        using var authed = AuthenticatedClient(expired);
        var response = await authed.GetAsync("/api/alexandria/auth/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Me_WithTokenForDeletedUser_Returns401()
    {
        var token = MintTokenForUser(Guid.NewGuid());

        using var authed = AuthenticatedClient(token);
        var response = await authed.GetAsync("/api/alexandria/auth/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Cross-cutting auth behavior ----------

    [TestMethod]
    public async Task AnyAlexandriaEndpoint_WithoutToken_Returns401()
    {
        var response = await Client.GetAsync("/api/alexandria/books");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AnyAlexandriaEndpoint_QueryStringToken_Accepted()
    {
        // The <img> tag can't set Authorization headers, so the handler accepts ?t=... as a fallback.
        var token = await LoginAsync();

        var response = await Client.GetAsync($"/api/alexandria/auth/me?t={Uri.EscapeDataString(token)}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class MeResponse
    {
        public string? UserUuid { get; set; }
        public string DisplayName { get; set; } = "";
        public string Username { get; set; } = "";
    }
}
