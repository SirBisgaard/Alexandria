using System.Net;
using System.Net.Http.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;

namespace Alexandria.Tests.Integration;

[TestClass]
public sealed class DevicesApiTests : IntegrationTestBase
{
    // ---------- GET /api/alexandria/devices ----------

    [TestMethod]
    public async Task List_NoDevices_ReturnsEmptyArray()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/devices");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<KoboDevice[]>();
        Assert.IsNotNull(body);
        Assert.IsEmpty(body);
    }

    [TestMethod]
    public async Task List_WithDevices_ReturnsAll()
    {
        await SeedDevice("Kobo Libra");
        await SeedDevice("Kobo Clara");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/devices");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<KoboDevice[]>();
        Assert.IsNotNull(body);
        Assert.HasCount(2, body);
        Assert.IsTrue(body.Any(d => d.Nickname == "Kobo Libra"));
        Assert.IsTrue(body.Any(d => d.Nickname == "Kobo Clara"));
    }

    // ---------- GET /api/alexandria/devices/generate-key ----------

    [TestMethod]
    public async Task GenerateKey_Returns200WithFreshGuid_DoesNotPersist()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var first = await authed.GetFromJsonAsync<GenerateKeyResponse>("/api/alexandria/devices/generate-key");
        var second = await authed.GetFromJsonAsync<GenerateKeyResponse>("/api/alexandria/devices/generate-key");

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreNotEqual(Guid.Empty, first.DeviceUuid);
        Assert.AreNotEqual(first.DeviceUuid, second.DeviceUuid);

        // And confirm that neither was persisted.
        var listResponse = await authed.GetAsync("/api/alexandria/devices");
        var list = await listResponse.Content.ReadFromJsonAsync<KoboDevice[]>();
        Assert.IsEmpty(list!);
    }

    // ---------- POST /api/alexandria/devices ----------

    [TestMethod]
    public async Task Create_WithExplicitUuid_Returns200AndPersists()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        var uuid = Guid.NewGuid();

        var response = await authed.PostAsJsonAsync("/api/alexandria/devices", new { nickname = "Kobo One", deviceUuid = uuid });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<KoboDevice>();
        Assert.IsNotNull(body);
        Assert.AreEqual(uuid, body.DeviceUuid);
        Assert.AreEqual("Kobo One", body.Nickname);
    }

    [TestMethod]
    public async Task Create_WithoutUuid_GeneratesOne()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PostAsJsonAsync("/api/alexandria/devices", new { nickname = "Generated" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<KoboDevice>();
        Assert.IsNotNull(body);
        Assert.AreNotEqual(Guid.Empty, body.DeviceUuid);
    }

    // ---------- PUT /api/alexandria/devices/{uuid} ----------

    [TestMethod]
    public async Task Update_Existing_Returns200WithUpdatedNickname()
    {
        var device = await SeedDevice("Original Name");
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/devices/{device.DeviceUuid}", new { nickname = "Renamed" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<KoboDevice>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Renamed", body.Nickname);
    }

    [TestMethod]
    public async Task Update_Unknown_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/devices/{Guid.NewGuid()}", new { nickname = "Nope" });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- DELETE /api/alexandria/devices/{uuid} ----------

    [TestMethod]
    public async Task Delete_Existing_Returns204AndRemoves()
    {
        var device = await SeedDevice("Doomed");
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.DeleteAsync($"/api/alexandria/devices/{device.DeviceUuid}");

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);

        var repo = GetService<IKoboDeviceRepository>();
        Assert.IsNull(await repo.GetByUuid(device.DeviceUuid));
    }

    [TestMethod]
    public async Task Delete_Unknown_StillReturns204()
    {
        // The repository Delete is a no-op when the UUID doesn't exist; the controller doesn't 404.
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.DeleteAsync($"/api/alexandria/devices/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---------- POST /api/alexandria/devices/{uuid}/unlock ----------

    [TestMethod]
    public async Task Unlock_Existing_Returns200AndClearsLockedFlag()
    {
        var device = await SeedDevice("Locked", locked: true);
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PostAsync($"/api/alexandria/devices/{device.DeviceUuid}/unlock", content: null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var repo = GetService<IKoboDeviceRepository>();
        var updated = await repo.GetByUuid(device.DeviceUuid);
        Assert.IsFalse(updated!.IsLocked);
    }

    [TestMethod]
    public async Task Unlock_Unknown_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PostAsync($"/api/alexandria/devices/{Guid.NewGuid()}/unlock", content: null);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<KoboDevice> SeedDevice(string nickname, bool locked = false)
    {
        var repo = GetService<IKoboDeviceRepository>();
        var device = new KoboDevice
        {
            DeviceUuid = Guid.NewGuid(),
            Nickname = nickname,
            IsLocked = locked
        };
        await repo.Create(device);
        if (locked)
            await repo.SetLocked(device.DeviceUuid, true);
        return (await repo.GetByUuid(device.DeviceUuid))!;
    }

    private sealed class GenerateKeyResponse
    {
        public Guid DeviceUuid { get; set; }
    }
}
