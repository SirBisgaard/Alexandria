using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.KoboStoreProxy.Services;

public class KoboProductService(IKoboProxyService koboProxyService, IAlexandriaLibraryService alexandriaLibraryService, ILogger<KoboProductService> logger) : IKoboProductService
{
    public async Task<KoboProxyResponse> Series(KoboProxyRequest request, Guid uuid)
    {
        var series = await alexandriaLibraryService.GetSeriesByUuid(uuid);
        if (series is null)
            return await koboProxyService.Proxy(request);

        logger.LogInformation("Series: serving {Count} books for local series '{Name}' ({Uuid})", series.Books.Count, series.Name, uuid);

        var items = series.Books.Select(KoboStoreEntityMapper.ToProductRecommendationItem).ToList();

        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, new KoboStoreProductRecommendations
        {
            Items = items,
            ItemCount = items.Count,
            TotalItemCount = items.Count,
            TotalPageCount = items.Count > 0 ? 1 : 0
        });
    }

    public Task<KoboProxyResponse> Recommendations(KoboProxyRequest request)
    {
        return Task.FromResult(KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, new KoboStoreProductRecommendations { ItemsPerPage = 50 }));
    }

    public async Task<KoboProxyResponse> NextRead(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
            return await koboProxyService.Proxy(request);

        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, new object());
    }

    public async Task<KoboProxyResponse> ProductRecommendations(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
            return await koboProxyService.Proxy(request);

        var recommendations = await alexandriaLibraryService.GetRecommendations(book.BookUuid);
        logger.LogInformation("ProductRecommendations: serving {Count} recommendations for '{Title}' ({Uuid})", recommendations.Count, book.Title, uuid);

        var items = recommendations.Select(KoboStoreEntityMapper.ToProductRecommendationItem).ToList();

        var response = new KoboStoreProductRecommendations
        {
            Items = items,
            ItemCount = items.Count,
            TotalItemCount = items.Count,
            TotalPageCount = items.Count > 0 ? 1 : 0
        };

        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, response);
    }

    public async Task<KoboProxyResponse> AudiobookProduct(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
            return await koboProxyService.Proxy(request);

        logger.LogInformation("AudiobookProduct: serving local product for '{Title}' ({Uuid})", book.Title, uuid);
        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, KoboStoreEntityMapper.ToAudiobookProduct(book));
    }

    public async Task<KoboProxyResponse> EbookProduct(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
            return await koboProxyService.Proxy(request);

        logger.LogInformation("EbookProduct: serving local product for '{Title}' ({Uuid})", book.Title, uuid);
        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, KoboStoreEntityMapper.ToEbookProduct(book));
    }

    public async Task<KoboProxyResponse> ProductPrices(KoboProxyRequest request, string uuids)
    {
        var uuidList = uuids.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var priceItems = new List<KoboStoreProductPriceItem>();
        var hasLocal = false;

        foreach (var id in uuidList)
        {
            if (!Guid.TryParse(id.Trim(), out var uuid))
                continue;

            var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
            if (book is null)
                continue;

            hasLocal = true;
            priceItems.Add(new KoboStoreProductPriceItem
            {
                Id = book.EntitlementUuid.ToString(),
                Price = new KoboStorePrice { Currency = "USD", Price = 0 }
            });
        }

        if (!hasLocal)
            return await koboProxyService.Proxy(request);

        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, new KoboStoreProductPrices { Items = priceItems });
    }
}
