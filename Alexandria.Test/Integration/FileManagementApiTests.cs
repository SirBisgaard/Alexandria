using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alexandria.Api.Models.Responses;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.Tests.TestData;

namespace Alexandria.Tests.Integration;

/// <summary>
/// End-to-end coverage for the file-management endpoints. Seeds the ebook folder with the
/// Peter Pan fixture, triggers a sync so the book is indexed, then exercises dry-run +
/// run paths through the HTTP surface.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FileManagementApiTests : IntegrationTestBase
{
    [TestMethod]
    public async Task DryRun_AfterTemplateSet_ReturnsPlanWithExpectedOperations()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        Fixtures.CopyEpubTo(EbookFolder);
        await TriggerSyncAndWait(authed);

        await authed.PutAsJsonAsync("/api/alexandria/settings", new
        {
            SyncBatchSize = 60,
            WordsPerPage = 250,
            FileRenameTemplate = "<Title>"
        });

        var response = await authed.PostAsync("/api/alexandria/file-management/dry-run", new StringContent(""));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var plan = await response.Content.ReadFromJsonAsync<FileManagementPlanResponse>();
        Assert.IsNotNull(plan);
        Assert.AreEqual("<Title>", plan.Template);
        Assert.IsEmpty(plan.Conflicts);
        Assert.HasCount(1, plan.Operations);
        Assert.EndsWith("Peter and Wendy.epub", plan.Operations[0].NewPath);
    }

    [TestMethod]
    public async Task DryRun_AlreadyNamedCorrectly_AppearsInSkipped()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var renamedPath = Path.Combine(EbookFolder, "Peter and Wendy.epub");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", Fixtures.EpubFileName), renamedPath);
        await TriggerSyncAndWait(authed);

        await authed.PutAsJsonAsync("/api/alexandria/settings", new
        {
            SyncBatchSize = 60,
            WordsPerPage = 250,
            FileRenameTemplate = "<Title>"
        });

        var response = await authed.PostAsync("/api/alexandria/file-management/dry-run", new StringContent(""));
        var plan = await response.Content.ReadFromJsonAsync<FileManagementPlanResponse>();

        Assert.IsEmpty(plan!.Operations);
        Assert.HasCount(1, plan.Skipped);
    }

    [TestMethod]
    public async Task Run_RenamesFileOnDisk()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var originalPath = Fixtures.CopyEpubTo(EbookFolder);
        await TriggerSyncAndWait(authed);

        await authed.PutAsJsonAsync("/api/alexandria/settings", new
        {
            SyncBatchSize = 60,
            WordsPerPage = 250,
            FileRenameTemplate = "<Title>"
        });

        using var runResponse = await authed.PostAsync("/api/alexandria/file-management", new StringContent(""));
        Assert.AreEqual(HttpStatusCode.OK, runResponse.StatusCode);
        await DrainStream(runResponse);

        Assert.IsFalse(File.Exists(originalPath));
        Assert.IsTrue(File.Exists(Path.Combine(EbookFolder, "Peter and Wendy.epub")));
    }

    [TestMethod]
    public async Task DryRun_WithoutAuth_Returns401()
    {
        var response = await Client.PostAsync("/api/alexandria/file-management/dry-run", new StringContent(""));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task TriggerSyncAndWait(HttpClient authed)
    {
        using var runResponse = await authed.PostAsync("/api/alexandria/sync", new StringContent(""));
        await DrainStream(runResponse);

        for (var i = 0; i < 50; i++)
        {
            var statusResponse = await authed.GetAsync("/api/alexandria/sync/status");
            var status = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
            if (!status.GetProperty("running").GetBoolean())
                return;
            await Task.Delay(100);
        }
    }

    private static async Task DrainStream(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync() is not null) { }
    }
}
