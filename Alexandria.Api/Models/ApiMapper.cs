using Alexandria.Api.Models.Requests;
using Alexandria.Api.Models.Responses;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;

namespace Alexandria.Api.Models;

public static class ApiMapper
{
    public static BookResponse ToBookResponse(Book book) => new()
    {
        BookUuid = book.BookUuid,
        EntitlementUuid = book.EntitlementUuid,
        CoverImageUuid = book.CoverImageUuid,
        Title = book.Title,
        Authors = book.Authors.Select(ToAuthorResponse).ToList(),
        Language = book.Language,
        PublicationDate = book.PublicationDate,
        Description = book.Description,
        Publisher = book.Publisher,
        Identifiers = book.Identifiers,
        Subjects = book.Subjects,
        Contributors = book.Contributors,
        Rights = book.Rights,
        Source = book.Source,
        Type = book.Type,
        Relation = book.Relation,
        Coverage = book.Coverage,
        Format = book.Format,
        Series = book.Series is not null ? ToSeriesResponse(book.Series) : null,
        HasCover = book.CoverFilePath is not null,
        FileName = book.FileName,
        KepubFileSize = book.KepubFileSize,
        CreatedAt = book.CreatedAt,
        ModifiedAt = book.ModifiedAt,
        IsArchived = book.IsArchived,
        ReadingProgress = book.ReadingProgress is not null ? ToReadingProgressResponse(book.ReadingProgress) : null
    };

    public static AuthorResponse ToAuthorResponse(Author author) => new()
    {
        AuthorUuid = author.AuthorUuid,
        Name = author.Name,
        CreatedAt = author.CreatedAt,
        ModifiedAt = author.ModifiedAt
    };

    public static AuthorDetailResponse ToAuthorDetailResponse(Author author) => new()
    {
        AuthorUuid = author.AuthorUuid,
        Name = author.Name,
        CreatedAt = author.CreatedAt,
        ModifiedAt = author.ModifiedAt,
        Books = author.Books.Select(ToBookResponse).ToList()
    };

    public static SeriesResponse ToSeriesResponse(Series series) => new()
    {
        SeriesUuid = series.SeriesUuid,
        Name = series.Name,
        Index = series.Index,
        CreatedAt = series.CreatedAt,
        ModifiedAt = series.ModifiedAt
    };

    public static SeriesDetailResponse ToSeriesDetailResponse(Series series) => new()
    {
        SeriesUuid = series.SeriesUuid,
        Name = series.Name,
        CreatedAt = series.CreatedAt,
        ModifiedAt = series.ModifiedAt,
        Books = series.Books.Select(ToBookResponse).ToList()
    };

    public static CollectionResponse ToCollectionResponse(BookCollection collection) => new()
    {
        CollectionUuid = collection.CollectionUuid,
        Name = collection.Name,
        BookCount = collection.BookUuids.Count,
        CreatedAt = collection.CreatedAt,
        ModifiedAt = collection.ModifiedAt
    };

    public static CollectionDetailResponse ToCollectionDetailResponse(CollectionWithBooks collection) => new()
    {
        CollectionUuid = collection.CollectionUuid,
        Name = collection.Name,
        Books = collection.Books.Select(ToBookResponse).ToList(),
        CreatedAt = collection.CreatedAt,
        ModifiedAt = collection.ModifiedAt
    };

    public static ReadingProgressResponse ToReadingProgressResponse(ReadingProgress progress) => new()
    {
        Status = progress.Status,
        ProgressPercent = progress.ProgressPercent,
        SpentReadingMinutes = progress.SpentReadingMinutes,
        RemainingTimeMinutes = progress.RemainingTimeMinutes,
        CreatedAt = progress.CreatedAt,
        ModifiedAt = progress.ModifiedAt
    };

    public static SettingsResponse ToSettingsResponse(AppSettings settings) => new()
    {
        LastIndexedTimestamp = settings.LastIndexedTimestamp,
        SyncBatchSize = settings.SyncBatchSize,
        ProxyLoggingEnabled = settings.ProxyLoggingEnabled
    };

    public static Author ToAuthor(UpdateAuthorRequest request) => new()
    {
        Name = request.Name.Trim()
    };

    public static Series ToSeries(UpdateSeriesRequest request) => new()
    {
        Name = request.Name.Trim()
    };

    public static AppSettings ToAppSettings(UpdateSettingsRequest request) => new()
    {
        SyncBatchSize = request.SyncBatchSize,
        ProxyLoggingEnabled = request.ProxyLoggingEnabled
    };

    public static Book ToBook(UpdateBookMetadataRequest request) => new()
    {
        Title = request.Title,
        Authors = request.Authors.Select(name => new Author { Name = name }).ToList(),
        Language = request.Language,
        PublicationDate = request.PublicationDate,
        Description = request.Description,
        Publisher = request.Publisher,
        Series = request.SeriesName is not null ? new Series { Name = request.SeriesName, Index = request.Index } : null
    };
}
