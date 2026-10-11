using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alexandria.Business.Library.Models.Settings;

namespace Alexandria.Tests.Integration;

/// <summary>
/// Comic rendering knobs are edited on the Settings page and persisted as JSON in the Settings
/// table. Pinned to sequential execution for the same reason as <see cref="SettingsApiTests"/>:
/// <c>SettingsService</c> caches settings process-wide.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ComicRenderingSettingsApiTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Get_FreshDatabase_ReturnsDefaultsForCurrentAndResetValues()
    {
        using var authed = AuthenticatedClient(await LoginAsync());

        var body = await authed.GetFromJsonAsync<JsonElement>("/api/alexandria/settings");

        var defaults = new ComicRenderingSettings();
        Assert.AreEqual(defaults.JpegQuality, body.GetProperty("comicRendering").GetProperty("jpegQuality").GetInt32());
        Assert.AreEqual(defaults.PreserveMarginPercent, body.GetProperty("comicRendering").GetProperty("preserveMarginPercent").GetDouble());
        Assert.AreEqual("Rotate", body.GetProperty("comicRendering").GetProperty("spreadHandling").GetString());
        Assert.AreEqual("Normal", body.GetProperty("comicRenderingDefaults").GetProperty("croppingStrength").GetString());
    }

    [TestMethod]
    public async Task Put_ComicRendering_IsPersistedAndReturned()
    {
        using var authed = AuthenticatedClient(await LoginAsync());
        var rendering = Rendering(new ComicRenderingSettings() with { JpegQuality = 90, SpreadHandling = ComicSpreadHandling.SplitAndRotate, MarginCropping = ComicMarginCropping.Off });

        using var response = await authed.PutAsJsonAsync("/api/alexandria/settings", Payload(rendering));

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await authed.GetFromJsonAsync<JsonElement>("/api/alexandria/settings");
        Assert.AreEqual(90, body.GetProperty("comicRendering").GetProperty("jpegQuality").GetInt32());
        Assert.AreEqual("SplitAndRotate", body.GetProperty("comicRendering").GetProperty("spreadHandling").GetString());
        Assert.AreEqual("Off", body.GetProperty("comicRendering").GetProperty("marginCropping").GetString());
        Assert.AreEqual(85, body.GetProperty("comicRenderingDefaults").GetProperty("jpegQuality").GetInt32());
    }

    [TestMethod]
    public async Task Put_WithoutComicRendering_KeepsStoredValues()
    {
        using var authed = AuthenticatedClient(await LoginAsync());
        (await authed.PutAsJsonAsync("/api/alexandria/settings", Payload(Rendering(new ComicRenderingSettings() with { JpegQuality = 70 })))).Dispose();

        using var response = await authed.PutAsJsonAsync("/api/alexandria/settings", Payload(null));

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        var body = await authed.GetFromJsonAsync<JsonElement>("/api/alexandria/settings");
        Assert.AreEqual(70, body.GetProperty("comicRendering").GetProperty("jpegQuality").GetInt32());
    }

    [TestMethod]
    public async Task Put_UnknownSpreadHandling_Returns400()
    {
        using var authed = AuthenticatedClient(await LoginAsync());
        var rendering = Rendering(new ComicRenderingSettings());
        rendering["spreadHandling"] = "Fold";

        using var response = await authed.PutAsJsonAsync("/api/alexandria/settings", Payload(rendering));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_OutOfRangeValue_Returns400KeyedToTheField()
    {
        using var authed = AuthenticatedClient(await LoginAsync());

        using var response = await authed.PutAsJsonAsync("/api/alexandria/settings", Payload(Rendering(new ComicRenderingSettings() with { JpegQuality = 0 })));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(body.GetProperty("errors").TryGetProperty("ComicRendering.JpegQuality", out _), body.ToString());
    }

    private static Dictionary<string, object?> Rendering(ComicRenderingSettings settings) => JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }))!;

    private static object Payload(Dictionary<string, object?>? comicRendering) => new
    {
        syncBatchSize = 60,
        proxyLoggingEnabled = false,
        wordsPerPage = 250,
        syncLibraryAtBoot = false,
        m4BFileTreatment = "Nothing",
        fileRenameTemplate = "<Authors> - <Title>",
        comicRendering
    };
}
