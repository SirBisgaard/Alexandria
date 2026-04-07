using Alexandria.Business.Extensions;

namespace Alexandria.Business.Models.Files;

public class EpubMetadata
{
    /// <summary>Persistent UUID stamped into the EPUB by Alexandria. Takes priority over the deterministic UUID.</summary>
    public Guid? AlexandriaUuid { get; set; }
    /// <summary>Series name. Read priority: EPUB3 belongs-to-collection > calibre:series.</summary>
    public string? SeriesName { get; set; }
    /// <summary>Position within the series. Read priority: EPUB3 group-position > calibre:series_index.</summary>
    public decimal? SeriesIndex { get; set; }
    /// <summary>dc:title — Book title (required).</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>dc:creator — Author names.</summary>
    public List<string> Authors { get; set; } = [];
    /// <summary>dc:language — Language code, e.g. "en" or "da" (required).</summary>
    public string Language { get; set; } = string.Empty;
    /// <summary>dc:date — Publication date string.</summary>
    public string? PublicationDate { get; set; }
    /// <summary>dc:description — Synopsis or description.</summary>
    public string? Description { get; set; }
    /// <summary>dc:publisher — Publisher name.</summary>
    public string? Publisher { get; set; }
    /// <summary>dc:identifier — Publication identifiers (ISBN, UUID, etc.).</summary>
    public List<string> Identifiers { get; set; } = [];
    /// <summary>dc:subject — Subjects, genres, or keywords.</summary>
    public List<string> Subjects { get; set; } = [];
    /// <summary>dc:contributor — Other contributors (editors, illustrators, translators).</summary>
    public List<string> Contributors { get; set; } = [];
    /// <summary>dc:rights — Copyright or usage rights information.</summary>
    public string? Rights { get; set; }
    /// <summary>dc:source — Source work from which this publication was derived.</summary>
    public string? Source { get; set; }
    /// <summary>dc:type — Nature or genre of the content.</summary>
    public string? Type { get; set; }
    /// <summary>dc:relation — Related resources.</summary>
    public string? Relation { get; set; }
    /// <summary>dc:coverage — Geographic or temporal scope.</summary>
    public string? Coverage { get; set; }
    /// <summary>dc:format — MIME type or resource format.</summary>
    public string? Format { get; set; }

    /// <summary>Path to the extracted cover image on disk.</summary>
    public string? CoverFilePath { get; set; }
    /// <summary>Generates a deterministic UUID v5 from title, authors, and series.</summary>
    public Guid GenerateDeterministicUuid()
    {
        var title = Title.ToLowerInvariant().Trim();
        var authors = string.Join(',', Authors.Select(a => a.ToLowerInvariant().Trim()));
        var series = SeriesName?.ToLowerInvariant().Trim() + "#" + SeriesIndex;

        return Guid.GenerateUuidV5($"{title}:{authors}:{series}");
    }

    /// <summary>Compares managed (editable) metadata fields. Pass-through DC elements are excluded.</summary>
    public bool MetadataEquals(EpubMetadata other)
    {
        return Title == other.Title
            && Language == other.Language
            && Normalize(PublicationDate) == Normalize(other.PublicationDate)
            && Normalize(Description) == Normalize(other.Description)
            && Normalize(Publisher) == Normalize(other.Publisher)
            && Normalize(SeriesName) == Normalize(other.SeriesName)
            && SeriesIndex == other.SeriesIndex
            && Authors.SequenceEqual(other.Authors, StringComparer.Ordinal);
    }

    private static string? Normalize(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
