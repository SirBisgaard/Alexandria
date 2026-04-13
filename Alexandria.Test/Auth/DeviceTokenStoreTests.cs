using Alexandria.Business.Auth;

namespace Alexandria.Tests.Auth;

[TestClass]
public sealed class DeviceTokenStoreTests
{
    private const string DeviceA = "device-a";
    private const string DeviceB = "device-b";

    private static AuthSecretProvider CreateSecrets()
    {
        var provider = new AuthSecretProvider();
        provider.Initialize(Guid.Parse("cd4631e2-04f2-4cc2-b938-b34c1e3c85a0"));
        return provider;
    }

    private static DeviceTokenStore CreateStore(FakeKoboDeviceRepository repo) => new(repo, CreateSecrets());

    [TestMethod]
    public async Task GetOrMint_persists_on_first_call_and_returns_stored_value_thereafter()
    {
        var repo = new FakeKoboDeviceRepository(DeviceA);
        var store = CreateStore(repo);

        var first = await store.GetOrMint(DeviceA);
        var second = await store.GetOrMint(DeviceA);

        Assert.AreEqual(first, second);
        var row = await repo.GetByDeviceId(DeviceA);
        Assert.AreEqual(first, row?.DeviceToken);
    }

    [TestMethod]
    public async Task GetOrMint_produces_distinct_tokens_per_device()
    {
        var repo = new FakeKoboDeviceRepository(DeviceA, DeviceB);
        var store = CreateStore(repo);

        var a = await store.GetOrMint(DeviceA);
        var b = await store.GetOrMint(DeviceB);

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public async Task Rotate_replaces_the_stored_token()
    {
        var repo = new FakeKoboDeviceRepository(DeviceA);
        var store = CreateStore(repo);
        var before = await store.GetOrMint(DeviceA);
        // Mint embeds exp=now+TTL; wait a second so the new token differs at second resolution.
        await Task.Delay(TimeSpan.FromSeconds(1));

        var rotated = await store.Rotate(DeviceA);
        var afterGet = await store.GetOrMint(DeviceA);

        Assert.AreNotEqual(before, rotated);
        Assert.AreEqual(rotated, afterGet);
    }

    [TestMethod]
    public async Task Rotate_on_device_with_no_prior_token_still_persists()
    {
        var repo = new FakeKoboDeviceRepository(DeviceA);
        var store = CreateStore(repo);

        var rotated = await store.Rotate(DeviceA);

        var row = await repo.GetByDeviceId(DeviceA);
        Assert.AreEqual(rotated, row?.DeviceToken);
    }
}
