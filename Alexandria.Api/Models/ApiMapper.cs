using System.Reflection;
using Alexandria.Api.Models.Requests;
using Alexandria.Api.Models.Responses;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
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
        File = new BookFileResponse
        {
            MediaType = book.File.MediaType,
            FileName = book.File.FileName,
            HasCover = book.File.CoverFilePath is not null,
            TotalFileSize = book.File.TotalFileSize,
            PageCount = book.File.PageCount,
            WordCount = book.File.WordCount,
            Duration = book.File.Duration,
            IsAbridged = book.File.IsAbridged,
            AudioCodec = book.File.AudioCodec,
            AudioBitrate = book.File.AudioBitrate,
            ReadingDirection = book.File.ReadingDirection,
            ColorMode = book.File.ColorMode
        },
        Subtitle = book.Subtitle,
        Isbn = book.Isbn,
        CreatedAt = book.CreatedAt,
        ModifiedAt = book.ModifiedAt,
        IsArchived = book.IsArchived,
        ReadingProgress = book.ReadingProgress is not null ? ToReadingProgressResponse(book.ReadingProgress) : null,
        Chapters = book.Chapters.Select(c => new AudiobookChapterResponse
        {
            ChapterIndex = c.ChapterIndex,
            Title = c.Title,
            Duration = c.Duration,
            StartTime = c.StartTime,
            EndTime = c.EndTime,
            FileName = c.FileName
        }).ToList()
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
        M4BFileTreatment = settings.M4BFileTreatment.ToString(),
        FileRenameTemplate = settings.FileRenameTemplate,
        Version = BuildVersion,
        ComicRendering = settings.ComicRendering
    };

    public static Author ToAuthor(UpdateAuthorRequest request) => new()
    {
        Name = request.Name.Trim()
    };

    public static Series ToSeries(UpdateSeriesRequest request) => new()
    {
        Name = request.Name.Trim()
    };

    /// <summary>
    /// <paramref name="currentComicRendering"/> is kept when the request omits the comic rendering
    /// section, so older clients and scripts don't reset it to defaults.
    /// </summary>
    public static AppSettings ToAppSettings(UpdateSettingsRequest request, ComicRenderingSettings currentComicRendering) => new()
    {
        SyncBatchSize = request.SyncBatchSize,
        ProxyLoggingEnabled = request.ProxyLoggingEnabled,
        WordsPerPage = request.WordsPerPage,
        SyncLibraryAtBoot = request.SyncLibraryAtBoot,
        M4BFileTreatment = request.M4BFileTreatment,
        FileRenameTemplate = request.FileRenameTemplate.Trim(),
        ComicRendering = request.ComicRendering is null ? currentComicRendering : ToComicRenderingSettings(request.ComicRendering)
    };

    public static ComicRenderingSettings ToComicRenderingSettings(ComicRenderingSettingsRequest request) => new()
    {
        SpreadHandling = request.SpreadHandling,
        MarginCropping = request.MarginCropping,
        CroppingStrength = request.CroppingStrength,
        PreserveMarginPercent = request.PreserveMarginPercent,
        JpegQuality = request.JpegQuality,
        AutoContrast = request.AutoContrast
    };

    public static FileManagementPlanResponse ToFileManagementPlanResponse(FileManagementPlan plan) => new()
    {
        Template = plan.Template,
        Operations = plan.Operations.Select(o => new FileManagementOperationResponse
        {
            BookUuid = o.BookUuid,
            Title = o.Title,
            MediaType = o.MediaType.ToString(),
            CurrentPath = o.CurrentPath,
            NewPath = o.NewPath,
            ChapterRenameCount = o.ChapterRenames.Count
        }).ToList(),
        Skipped = plan.Skipped.Select(s => new FileManagementSkippedResponse
        {
            BookUuid = s.BookUuid,
            Title = s.Title,
            CurrentPath = s.CurrentPath
        }).ToList(),
        Conflicts = plan.Conflicts
    };

    /// <summary>
    /// Applies every editable field from <paramref name="request"/> onto <paramref name="book"/>
    /// in place. The DTO is treated as a full snapshot of editable state — nullable fields
    /// overwrite unconditionally, so the frontend must send the current value of every field
    /// on every save. Fields the DTO does not carry (Subtitle, Isbn, Identifiers, Subjects,
    /// Rights, Source, Type, Relation, Coverage, Format) are untouched and retain their DB values.
    /// </summary>
    public static void ApplyToBook(UpdateBookMetadataRequest request, Book book)
    {
        book.Title = request.Title;
        book.Authors = request.Authors.Select(name => new Author { Name = name }).ToList();
        book.Language = request.Language ?? string.Empty;
        book.PublicationDate = request.PublicationDate;
        book.Description = request.Description;
        book.Publisher = request.Publisher;
        book.File.IsAbridged = request.IsAbridged;
        if (book.File.MediaType == MediaType.Comic)
        {
            book.File.ReadingDirection = request.ReadingDirection ?? book.File.ReadingDirection;
            book.File.ColorMode = request.ColorMode ?? book.File.ColorMode;
        }
        book.Contributors = request.Contributors?.Select(c => new Contributor { Name = c.Name.Trim(), Role = c.Role?.Trim() }).ToList() ?? [];
        book.Series = !string.IsNullOrEmpty(request.SeriesName) ? new Series { Name = request.SeriesName, Index = request.Index } : null;
    }
}
