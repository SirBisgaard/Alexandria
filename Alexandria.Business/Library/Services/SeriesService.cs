using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Helpers;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Services;

public class SeriesService(ISeriesRepository seriesRepository, IBookService bookService, IIndexerService indexerService) : ISeriesService
{
    public async Task<List<Series>> GetSeries(SeriesSortOptions? sortOptions = null)
    {
        var allSeries = await seriesRepository.GetAll();
        var books = await bookService.GetBooks();
        foreach (var series in allSeries)
        {
            series.Books = books.Where(b => b.Series?.SeriesUuid == series.SeriesUuid).OrderBy(b => b.Series?.Index ?? 0).ToList();
        }
        var populatedSeries = allSeries.Where(s => s.Books.Count > 0).ToList();
        return AlexandriaLibraryHelper.SortSeries(populatedSeries, sortOptions ?? new SeriesSortOptions());
    }

    public async Task<Series?> GetSeriesByUuid(Guid uuid, BookSortOptions? sortOptions = null)
    {
        var series = await seriesRepository.FindByUuid(uuid);
        if (series is null)
        {
            return null;
        }
        var books = await bookService.GetBooks();
        series.Books = books.Where(b => b.Series?.SeriesUuid == series.SeriesUuid).ToList();
        series.Books = AlexandriaLibraryHelper.SortBooks(series.Books, sortOptions ?? new BookSortOptions { Sort = BookSortField.BookNumber, Order = SortOrder.Asc });
        return series;
    }

    public async Task<Series?> GetSeriesByName(string name)
    {
        return await seriesRepository.FindByName(name);
    }

    public async Task<Series?> UpdateSeries(Series series)
    {
        var existing = await seriesRepository.FindByUuid(series.SeriesUuid);
        if (existing is null)
            return null;

        await seriesRepository.Update(series);

        var books = await bookService.GetBooks();
        var seriesBooks = books.Where(b => b.Series?.SeriesUuid == series.SeriesUuid).ToList();

        foreach (var book in seriesBooks)
            await indexerService.ReindexBook(book.BookUuid, book);

        return await GetSeriesByUuid(series.SeriesUuid);
    }

    public async Task<Series?> MergeSeries(Guid sourceUuid, Guid targetUuid)
    {
        var books = await bookService.GetBooks();
        var affectedBooks = books.Where(b => b.Series?.SeriesUuid == sourceUuid).ToList();
        await seriesRepository.ReassignBooks(sourceUuid, targetUuid);
        await seriesRepository.RemoveOrphanedSeries();

        books = await bookService.GetBooks();
        foreach (var book in books.Where(b => affectedBooks.Any(ab => ab.BookUuid == b.BookUuid)))
            await indexerService.ReindexBook(book.BookUuid, book);

        return await GetSeriesByUuid(targetUuid);
    }
}
