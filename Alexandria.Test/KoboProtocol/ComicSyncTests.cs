using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals;
using Alexandria.Tests.TestData;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// Comics reach the device as fixed-layout EPUB renditions built for its screen. The protocol
/// contract: Format "EPUB3FL" (Nickel only opens synced books in the comic reader when the
/// format says fixed layout), a device-scoped <c>/epub</c> download URL, and a Size equal to the
/// exact rendition that URL serves. A comic without a rendition for the device is held back.
/// </summary>
[TestClass]
public sealed class ComicSyncTests : KoboProtocolTestBase
{
    private static readonly ComicDeviceProfile LibraColour = ComicDeviceProfile.ForModelCode("0390")!;

    [TestMethod]
    public async Task Sync_ComicWithRendition_IsSentAsFixedLayoutEpubWithRenditionSize()
    {
        await SeedRecordedDevice();
        var book = await SeedComic();
        var rendition = await WriteRendition(book, 12_345);
        Upstream.RegisterEmptyJsonArray(HttpMethod.Get, "/v1/library/sync");

        using var response = await SendSync();

        await AssertStatusAsync(response, HttpStatusCode.OK, "comic sync");
        var item = FindNewEntitlement(await ReadJsonAsync(response), book.EntitlementUuid);
        Assert.IsNotNull(item, "the comic must be sent as a NewEntitlement");
        var entitlement = item.Value.GetProperty("NewEntitlement");
        Assert.IsTrue(entitlement.TryGetProperty("BookEntitlement", out var bookEntitlement), "comics use the ebook entitlement slot");
        Assert.AreEqual("Imported", bookEntitlement.GetProperty("OriginCategory").GetString());
        var downloadUrl = entitlement.GetProperty("BookMetadata").GetProperty("DownloadUrls")[0];
        Assert.AreEqual("EPUB3FL", downloadUrl.GetProperty("Format").GetString());
        Assert.AreEqual($"http://localhost/api/kobo/{RecordedDeviceUuid}/download/{book.EntitlementUuid}/epub", downloadUrl.GetProperty("Url").GetString());
        Assert.AreEqual(rendition.Length, downloadUrl.GetProperty("Size").GetInt64());
    }

    [TestMethod]
    public async Task Sync_ComicWithoutRendition_IsHeldBackAndStaysUnsynced()
    {
        await SeedRecordedDevice();
        var book = await SeedComic();
        Upstream.RegisterEmptyJsonArray(HttpMethod.Get, "/v1/library/sync");

        using var response = await SendSync();

        await AssertStatusAsync(response, HttpStatusCode.OK, "comic sync without rendition");
        Assert.IsNull(FindNewEntitlement(await ReadJsonAsync(response), book.EntitlementUuid));
        var synced = await GetService<IKoboDeviceSyncRepository>().GetSyncedModifiedDates(RecordedDeviceUuid.ToString());
        Assert.IsFalse(synced.ContainsKey(book.BookUuid), "a held-back comic must be offered again on the next sync");
    }

    [TestMethod]
    public async Task Download_Comic_ServesTheDeviceRendition()
    {
        await SeedRecordedDevice();
        var book = await SeedComic();
        var rendition = await WriteRendition(book, 2_048);

        using var request = DeviceRequest(HttpMethod.Get, $"/download/{book.EntitlementUuid}/epub");
        using var response = await Client.SendAsync(request);

        await AssertStatusAsync(response, HttpStatusCode.OK, "comic download");
        Assert.AreEqual("application/epub+zip", response.Content.Headers.ContentType?.MediaType);
        StringAssert.EndsWith(response.Content.Headers.ContentDisposition?.FileNameStar, ".epub");
        CollectionAssert.AreEqual(await File.ReadAllBytesAsync(rendition.FullName), await response.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task Download_ComicWithoutRendition_Returns404()
    {
        await SeedRecordedDevice();
        var book = await SeedComic();

        using var request = DeviceRequest(HttpMethod.Get, $"/download/{book.EntitlementUuid}/epub");
        using var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Metadata_Comic_ReportsRenditionSize()
    {
        await SeedRecordedDevice();
        var book = await SeedComic();
        var rendition = await WriteRendition(book, 4_096);

        using var request = DeviceRequest(HttpMethod.Get, $"/v1/library/{book.EntitlementUuid}/metadata");
        using var response = await Client.SendAsync(request);

        await AssertStatusAsync(response, HttpStatusCode.OK, "comic metadata");
        var downloadUrl = (await ReadJsonAsync(response))[0].GetProperty("DownloadUrls")[0];
        Assert.AreEqual("EPUB3FL", downloadUrl.GetProperty("Format").GetString());
        Assert.AreEqual(rendition.Length, downloadUrl.GetProperty("Size").GetInt64());
    }

    private async Task<Book> SeedComic()
    {
        var path = ComicFixtures.CreateSimpleCbz(EbookFolder, "Ann - Saga #1 - Pages.cbz");
        var book = new CbzFileHandler().ExtractMetadata(path);
        book.BookUuid = Guid.NewGuid();
        book.EntitlementUuid = Guid.NewGuid();
        book.CreatedAt = DateTimeOffset.UtcNow;
        book.ModifiedAt = DateTimeOffset.UtcNow;
        book.File.BookUuid = book.BookUuid;
        book.File.SourcePath = path;
        book.File.TotalFileSize = new FileInfo(path).Length;
        book.Series = null;
        await GetService<IAuthorRepository>().GetOrCreate("Ann");
        await GetService<IBookRepository>().Add(book);
        await GetService<IAuthorRepository>().SetBookAuthors(book.BookUuid, ["Ann"]);
        return book;
    }

    private async Task<FileInfo> WriteRendition(Book book, int size)
    {
        var path = await GetService<IComicRenditionStore>().GetRenditionPath(book, LibraColour);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, size).Select(i => (byte)i).ToArray());
        return new FileInfo(path);
    }

    private async Task<HttpResponseMessage> SendSync()
    {
        using var request = DeviceRequest(HttpMethod.Get, "/v1/library/sync?Filter=ALL&DownloadUrlFilter=Generic,Android&PrioritizeRecentReads=true");
        return await Client.SendAsync(request);
    }

    private static HttpRequestMessage DeviceRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, DeviceUrl(relativePath));
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static JsonElement? FindNewEntitlement(JsonElement body, Guid entitlementUuid)
    {
        foreach (var item in body.EnumerateArray())
        {
            if (item.TryGetProperty("NewEntitlement", out var entitlement)
                && entitlement.TryGetProperty("BookEntitlement", out var bookEntitlement)
                && bookEntitlement.GetProperty("Id").GetString() == entitlementUuid.ToString())
                return item;
        }
        return null;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
