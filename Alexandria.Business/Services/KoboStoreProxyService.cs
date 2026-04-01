using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.KoboStore;
using Alexandria.Business.Models.Settings;
using Microsoft.AspNetCore.Http;

namespace Alexandria.Business.Services;

public class KoboStoreProxyService(KoboProxySettings settings, HttpClient httpClient, IAlexandriaLibraryService alexandriaLibraryService) : IKoboProxyService
{   
    // Headers that shouldn't be forwarded in either direction
    private static readonly HashSet<string> HopByHopHeaders =
    [
        "connection", "keep-alive", "transfer-encoding",
        "te", "trailer", "upgrade", "host"
    ];

    public async Task<KoboProxyResponse> Initialization(KoboProxyRequest request)
    {
        var koboResponse = await Proxy(request);
        
        var json = JsonSerializer.Deserialize<JsonObject>(koboResponse.Body);
        var resources = json?["Resources"]?.AsObject();

        if (resources is null)
            return koboResponse;

        // Override only the endpoints we need to intercept
        resources["library_sync"] = $"{settings.AlexandriaApi}/api/kobo/v1/library/sync";
        resources["image_host"] = $"{settings.AlexandriaApi}/api/kobo";
        resources["image_url_template"] = $"{settings.AlexandriaApi}/api/kobo/{{ImageId}}/{{Width}}/{{Height}}/false/image.jpg";
        resources["image_url_quality_template"] = $"{settings.AlexandriaApi}/api/kobo/{{ImageId}}/{{Width}}/{{Height}}/{{Quality}}/false/image.jpg";

        
        var headers = new Dictionary<string, string[]> { { "x-kobo-apitoken", ["e30="] } };
        var jsonContent = json?.ToString() ?? "{}";
        var body = new StringContent(jsonContent, Encoding.UTF8, new MediaTypeHeaderValue("application/json; charset=utf-8"));
        
        return new KoboProxyResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Headers = headers,
            Body = await body.ReadAsByteArrayAsync()
        };
    }

    public async Task<KoboProxyResponse> LibrarySync(KoboProxyRequest request)
    {
        // 1. Forward to Kobo and get their sync results
        var koboResponse = await Proxy(request);

        var syncResults = new List<SyncResultItem>();

        // Kobo store results — deserialize as raw JSON objects and keep as-is
        if (koboResponse.StatusCode == 200)
        {
            var storeResults = JsonSerializer.Deserialize<List<SyncResultItem>>(koboResponse.Body);

            if (storeResults is not null)
                syncResults.AddRange(storeResults);
        }

        // Local books — fully typed
        var books = await alexandriaLibraryService.GetBooks();
        foreach (var book in books)
        {
            syncResults.Add(new SyncResultItem
            {
                NewEntitlement = new Entitlement
                {
                    BookEntitlement = book.ToEntitlement(),
                    BookMetadata = book.ToMetadata(settings.AlexandriaApi)
                }
            });
        }
  
        var jsonContent = JsonSerializer.Serialize(syncResults);
        var body = new StringContent(jsonContent, Encoding.UTF8, new MediaTypeHeaderValue("application/json; charset=utf-8"));
        
        return new KoboProxyResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Headers = koboResponse.Headers,
            Body = await body.ReadAsByteArrayAsync()
        };
    }

    public async Task<KoboProxyResponse> Download(KoboProxyRequest request, string uuid)
    {
        // TODO: FIX
        var book = await _library.GetBookByUuidAsync(uuid);
        if (book is not null)
        {
            var stream = System.IO.File.OpenRead(book.KepubFilePath);
            return File(stream, "application/epub+zip", 
                $"{book.Title}.kepub.epub");
        }

        // Not our book — proxy to Kobo
        return await Proxy(request);
    }

    public Task<KoboProxyResponse> CoverImage(KoboProxyRequest request, string uuid, int width, int height, string? quality = null, string? greyscale = null)
    {   
        // TODO: FIX
        var book = await _library.GetBookByUuidAsync(uuid);
        if (book?.CoverFilePath is not null && System.IO.File.Exists(book.CoverFilePath))
        {
            var stream = System.IO.File.OpenRead(book.CoverFilePath);
            return File(stream, "image/jpeg");
        }

        // Not our book — redirect to Kobo CDN
        return Redirect($"https://cdn.kobo.com/book-images/{uuid}/{width}/{height}/false/image.jpg");
    }

    public async Task<KoboProxyResponse> ReadingState(KoboProxyRequest request, string uuid)
    {
        // TODO: FIX
        var book = await _library.GetBookByUuidAsync(uuid);
        if (book is not null)
        {
            if (Request.Method == "PUT")
            {
                // Parse and store reading progress locally
                await _library.UpdateReadingStateAsync(uuid, HttpContext.Request);
            }
            return Ok(new[] { BuildReadingState(book) });
        }
        
        // Not our book — proxy to Kobo
        return await Proxy(request);
    }

    public async Task<KoboProxyResponse> Metadata(KoboProxyRequest request, string uuid)
    {
        // TODO: FIX
        var book = await _library.GetBookByUuidAsync(uuid);
        if (book is not null)
            return Ok(new[] { BuildMetadata(book) });
        
        // Not our book — proxy to Kobo
        return await Proxy(request);
    }

    public async Task<KoboProxyResponse> DeleteBook(KoboProxyRequest request, string uuid)
    {
        // TODO: FIX
        var book = await _library.GetBookByUuidAsync(uuid);
        if (book is not null)
        {
            // Mark as archived locally, don't delete the file
            await _library.ArchiveBookAsync(uuid);
            return StatusCode(204);
        }

        return await ProxyToKobo();
    }
    
    public async Task<KoboProxyResponse> Proxy(KoboProxyRequest request)
    {
        var targetUrl = $"{settings.StoreApi}{request.Path}{request.QueryString}";
        var upstream = new HttpRequestMessage(new HttpMethod(request.Method), targetUrl);

        // Copy body
        if (request.Body.Length > 0)
            upstream.Content = new ByteArrayContent(request.Body);

        // Copy headers
        foreach (var header in request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key.ToLowerInvariant()))
                continue;

            // Content headers go on the content, not the request
            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
                && upstream.Content is not null)
            {
                upstream.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            else
            {
                upstream.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Send to Kobo
        var response = await httpClient.SendAsync(upstream);

        // Read the full response
        var responseBody = await response.Content.ReadAsByteArrayAsync();

        var responseHeaders = response.Headers
            .Concat(response.Content.Headers)
            .ToDictionary(h => h.Key, h => h.Value.ToArray());

        return new KoboProxyResponse
        {
            StatusCode = (int)response.StatusCode,
            Headers = responseHeaders,
            Body = responseBody
        };
    }
    
    private void CopyKoboSyncHeaders(KoboProxyResponse koboResponse)
    {
        // These headers control sync continuation and reading state
       
    }
}