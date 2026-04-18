using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Alexandria.Business.Authentication;
using Alexandria.Business.Authentication.Interfaces;
using Alexandria.Business.Library.Services;
using Alexandria.DataAccess.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Alexandria.Tests.Integration;

/// <summary>
/// Shared fixture for Section 3 Integration tests. Each test method gets its own
/// <see cref="WebApplicationFactory{TEntryPoint}"/>, its own temp directory (data /
/// ebooks / audiobooks subfolders) and an <see cref="HttpClient"/> with
/// <c>AllowAutoRedirect = false</c> so redirect responses are testable. The default
/// <c>PasswordHasher</c> is swapped for a fast fake so the first-boot <c>alexander</c>
/// user seed doesn't pay the real PBKDF2 cost. All state is torn down in
/// <c>[TestCleanup]</c> — factory disposed, temp directory wiped.
/// </summary>
public abstract class IntegrationTestBase
{
    protected WebApplicationFactory<Program> Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;
    protected string TempDir { get; private set; } = null!;
    protected string EbookFolder => Path.Combine(TempDir, "ebooks");
    protected string AudiobookFolder => Path.Combine(TempDir, "audiobooks");

    [TestInitialize]
    public void IntegrationTestBase_Setup()
    {
        // SettingsService caches AppSettings in a static field that persists across factories
        // — clear it so this test's factory always reads from its own DB.
        ResetSettingsServiceCache();

        TempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(TempDir, "data"));
        Directory.CreateDirectory(EbookFolder);
        Directory.CreateDirectory(AudiobookFolder);

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AlexandriaSettings:PublicUrl"] = "http://localhost",
                        ["AlexandriaSettings:DataFolder"] = Path.Combine(TempDir, "data"),
                        ["AlexandriaSettings:EbookFolder"] = EbookFolder,
                        ["AlexandriaSettings:AudiobookFolder"] = AudiobookFolder
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IPasswordHasher, FakePasswordHasher>();
                    // DatabaseFlushService races with test teardown — it fires Flush on StopAsync,
                    // which opens a second SqliteConnection to the same file and calls BackupDatabase
                    // while the connection can still have in-flight controller work. Tests don't need
                    // durable persistence, so remove the hosted service entirely.
                    var flushHost = services.SingleOrDefault(s => s.ImplementationType == typeof(DatabaseFlushService));
                    if (flushHost is not null)
                        services.Remove(flushHost);
                });
            });

        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [TestCleanup]
    public void IntegrationTestBase_Cleanup()
    {
        Client?.Dispose();
        Factory?.Dispose();
        if (Directory.Exists(TempDir))
        {
            try
            {
                Directory.Delete(TempDir, recursive: true);
            }
            catch (IOException)
            {
                // Sqlite WAL files can hold the directory briefly under parallel test pressure.
                // Swallowing is acceptable: the temp root is unique per test.
            }
        }
    }

    /// <summary>POST /api/alexandria/auth/login and return the token field.</summary>
    protected async Task<string> LoginAsync(string username = "alexander", string password = "TheGreat")
    {
        var response = await Client.PostAsJsonAsync("/api/alexandria/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    /// <summary>Clone of <see cref="Client"/> with a Bearer token pre-attached.</summary>
    protected HttpClient AuthenticatedClient(string token)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Mints a token for an arbitrary user UUID. Bypasses login — useful for expired-token
    /// and unknown-user edge cases. <paramref name="ttl"/> defaults to the production user-token TTL.
    /// </summary>
    protected string MintTokenForUser(Guid userUuid, TimeSpan? ttl = null)
    {
        var key = GetService<AuthSecretProvider>().GetUserHmacKey();
        return TokenHelpers.Mint(userUuid.ToString(), key, ttl ?? TokenHelpers.UserTokenTtl);
    }

    /// <summary>Resolves a service from the factory's DI container.</summary>
    protected T GetService<T>() where T : notnull
    {
        var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    private static void ResetSettingsServiceCache()
    {
        typeof(SettingsService)
            .GetField("cached", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
    }

    protected sealed class LoginResponse
    {
        public Guid UserUuid { get; set; }
        public string DisplayName { get; set; } = "";
        public string Token { get; set; } = "";
    }

    /// <summary>
    /// Replaces the real PBKDF2 hasher during tests. The first-boot <c>alexander</c> / <c>TheGreat</c>
    /// seed is written with <c>fake:TheGreat</c>, and login verifies by string equality — no crypto cost.
    /// </summary>
    internal sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"fake:{password}";
        public bool Verify(string password, string stored) => stored == $"fake:{password}";
    }
}
