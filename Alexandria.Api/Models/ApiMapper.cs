using System.Reflection;
using Alexandria.Api.Models.Requests;
using Alexandria.Api.Models.Responses;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;

namespace Alexandria.Api.Models;

public static class ApiMapper
{
    private static readonly string? BuildVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    
    public static BookResponse ToBookResponse(Book book) => new()
    {
        BookUuid = book.BookUuid,
        EntitlementUuid = book.EntitlementUuid,
        Title = book.Title,
        Authors = book.Authors.Select(ToAuthorResponse).ToList(),
        Language = book.Language,
        PublicationDate = book.PublicationDate,
        Description = book.Description,
        Publisher = book.Publisher,
        Identifiers = book.Identifiers,
        Subjects = book.Subjects,
        Contributors = book.Contributors.Select(c => new ContributorResponse { Name = c.Name, Role = c.Role }).ToList(),
        Rights = book.Rights,
        Source = book.Source,
        Type = book.Type,
        Relation = book.Relation,
        Coverage = book.Coverage,
        Format = book.Format,
        Series = book.Series is not null ? ToSeriesResponse(book.Series) : null,
        HasCover = book.CoverFilePath is not null,
        FileName = book.FileName,
        MediaType = book.MediaType,
        Subtitle = book.Subtitle,
        Isbn = book.Isbn,
        TotalFileSize = book.TotalFileSize,
        PageCount = book.PageCount,
        WordCount = book.WordCount,
        Duration = book.Duration,
        IsAbridged = book.IsAbridged,
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

    public static CollectionResponse ToCollectionResponse(BookCollection collection, Dictionary<Guid, MediaType> bookMediaTypes)
    {
        var audiobookCount = collection.BookUuids.Count(id => bookMediaTypes.TryGetValue(id, out var mt) && mt == MediaType.Audiobook);
        return new CollectionResponse
        {
            CollectionUuid = collection.CollectionUuid,
            Name = collection.Name,
            EbookCount = collection.BookUuids.Count - audiobookCount,
            AudiobookCount = audiobookCount,
            CreatedAt = collection.CreatedAt,
            ModifiedAt = collection.ModifiedAt
        };
    }

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
        ProxyLoggingEnabled = settings.ProxyLoggingEnabled,
        WordsPerPage = settings.WordsPerPage,
        SyncLibraryAtBoot = settings.SyncLibraryAtBoot,
        Version = BuildVersion
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
        ProxyLoggingEnabled = request.ProxyLoggingEnabled,
        WordsPerPage = request.WordsPerPage,
        SyncLibraryAtBoot = request.SyncLibraryAtBoot
    };

    public static Book ToBook(UpdateBookMetadataRequest request) => new()
    {
        Title = request.Title,
        Authors = request.Authors.Select(name => new Author { Name = name }).ToList(),
        Language = request.Language,
        PublicationDate = request.PublicationDate,
        Description = request.Description,
        Publisher = request.Publisher,
        IsAbridged = request.IsAbridged,
        Series = request.SeriesName is not null ? new Series { Name = request.SeriesName, Index = request.Index } : null
    };
}
