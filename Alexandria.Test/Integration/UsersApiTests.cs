using System.Net;
using System.Net.Http.Json;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.Tests.Integration;

[TestClass]
public sealed class UsersApiTests : IntegrationTestBase
{
    // ---------- GET /api/alexandria/users ----------

    [TestMethod]
    public async Task List_SeededAdmin_ReturnsOneUser()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/users");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserDto[]>();
        Assert.IsNotNull(body);
        Assert.HasCount(1, body);
        Assert.AreEqual("alexander", body[0].Username);
        Assert.AreEqual("Alexander The Great", body[0].DisplayName);
    }

    [TestMethod]
    public async Task List_ProjectionDoesNotIncludePasswordHash()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/users");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.IsFalse(raw.Contains("fake:", StringComparison.OrdinalIgnoreCase), $"Response body unexpectedly contained a password hash: {raw}");
        Assert.IsFalse(raw.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- POST /api/alexandria/users ----------

    [TestMethod]
    public async Task Create_Valid_Returns200AndPersists()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PostAsJsonAsync("/api/alexandria/users", new
        {
            username = "new-user",
            password = "hunter2",
            displayName = "Newcomer"
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual("new-user", body.Username);
        Assert.AreEqual("Newcomer", body.DisplayName);

        // Can now log in with those credentials.
        var loginResponse = await Client.PostAsJsonAsync("/api/alexandria/auth/login", new { username = "new-user", password = "hunter2" });
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [TestMethod]
    public async Task Create_DuplicateUsername_Returns400()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PostAsJsonAsync("/api/alexandria/users", new
        {
            username = "alexander",
            password = "whatever",
            displayName = "Duplicate"
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- PUT /api/alexandria/users/{uuid} ----------

    [TestMethod]
    public async Task Update_Existing_Returns200WithUpdatedFields()
    {
        var userRepo = GetService<IUserRepository>();
        var alexander = (await userRepo.GetByUsername("alexander"))!;
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/users/{alexander.UserUuid}", new
        {
            username = "alex",
            password = (string?)null,
            displayName = "Alex The Just-OK"
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual("alex", body.Username);
        Assert.AreEqual("Alex The Just-OK", body.DisplayName);
    }

    [TestMethod]
    public async Task Update_OmittingPassword_PreservesHash_OriginalPasswordStillLogsIn()
    {
        var userRepo = GetService<IUserRepository>();
        var alexander = (await userRepo.GetByUsername("alexander"))!;
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        await authed.PutAsJsonAsync($"/api/alexandria/users/{alexander.UserUuid}", new
        {
            username = "alexander",
            password = (string?)null,
            displayName = "Name Only Changed"
        });

        var loginResponse = await Client.PostAsJsonAsync("/api/alexandria/auth/login", new { username = "alexander", password = "TheGreat" });
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [TestMethod]
    public async Task Update_WithPassword_RotatesHash_NewPasswordLogsIn()
    {
        var userRepo = GetService<IUserRepository>();
        var alexander = (await userRepo.GetByUsername("alexander"))!;
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        await authed.PutAsJsonAsync($"/api/alexandria/users/{alexander.UserUuid}", new
        {
            username = "alexander",
            password = "RotatedPass",
            displayName = "Alexander The Great"
        });

        var oldLogin = await Client.PostAsJsonAsync("/api/alexandria/auth/login", new { username = "alexander", password = "TheGreat" });
        var newLogin = await Client.PostAsJsonAsync("/api/alexandria/auth/login", new { username = "alexander", password = "RotatedPass" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [TestMethod]
    public async Task Update_Unknown_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/users/{Guid.NewGuid()}", new
        {
            username = "ghost",
            password = (string?)null,
            displayName = "Ghost"
        });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- DELETE /api/alexandria/users/{uuid} ----------

    [TestMethod]
    public async Task Delete_AnotherUser_Returns204()
    {
        // Seed a second user, log in as alexander, delete the second user.
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        var createResponse = await authed.PostAsJsonAsync("/api/alexandria/users", new
        {
            username = "second",
            password = "abc",
            displayName = "Second"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<UserDto>();

        var response = await authed.DeleteAsync($"/api/alexandria/users/{created!.UserUuid}");

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task Delete_Self_Returns400()
    {
        var userRepo = GetService<IUserRepository>();
        var alexander = (await userRepo.GetByUsername("alexander"))!;

        // Seed a second user so the last-user guard doesn't fire first.
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        await authed.PostAsJsonAsync("/api/alexandria/users", new
        {
            username = "other",
            password = "abc",
            displayName = "Other"
        });

        var response = await authed.DeleteAsync($"/api/alexandria/users/{alexander.UserUuid}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Delete_LastUser_Returns400()
    {
        // With only the seeded admin present, attempting to delete any user hits the "last user" guard.
        // We delete a non-self UUID so the self-guard (which runs first) doesn't short-circuit.
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.DeleteAsync($"/api/alexandria/users/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class UserDto
    {
        public Guid UserUuid { get; set; }
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }
}
