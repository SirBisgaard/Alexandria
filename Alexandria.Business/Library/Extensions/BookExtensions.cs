using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Library.Extensions;

public static class BookExtensions
{
    extension(Book book)
    {
        /// <summary>Generates a format-agnostic UUID v5 from title and authors only, for linking ebook/audiobook editions of the same work.</summary>
        public Guid GenerateWorkUuid()
        {
            var title = book.Title.ToLowerInvariant().Trim();
            var authors = string.Join(',', book.Authors.Select(a => a.Name.ToLowerInvariant().Trim()));
            return Guid.GenerateUuidV5($"work:{title}:{authors}");
        }

        /// <summary>
        /// Generates a deterministic UUID v5 from all stable metadata fields: title, subtitle, authors,
        /// language, ISBN, identifiers, publisher, publication date, contributors, and media type.
        /// Two books only collide if every field matches exactly.
        /// </summary>
        public Guid GenerateBookUuid()
        {
            var title = book.Title.ToLowerInvariant().Trim();
            var authors = string.Join(',', book.Authors.Select(a => a.Name.ToLowerInvariant().Trim()).Order());
            var mediaType = book.File.MediaType.ToString().ToLowerInvariant();
            return Guid.GenerateUuidV5($"book:{title}:{authors}:{mediaType}");
        }

        /// <summary>
        /// Deterministic UUID v5 for the current cover image, derived from BookUuid plus
        /// ModifiedAt unix seconds. Rotates on every reindex so the Kobo device can't serve
        /// a stale URL-cached cover after the user edits the book or cover.
        /// </summary>
        public Guid GenerateCoverImageUuid() => BookExtensions.CoverImageUuid(book.BookUuid, book.ModifiedAt);

        /// <summary>Compares managed (editable) metadata fields. Pass-through DC elements are excluded.</summary>
        public bool MetadataEquals(Book other)
        {
            return book.Title == other.Title
                && book.Language == other.Language
                && Normalize(book.PublicationDate) == Normalize(other.PublicationDate)
                && Normalize(book.Description) == Normalize(other.Description)
                && Normalize(book.Publisher) == Normalize(other.Publisher)
                && Normalize(book.Subtitle) == Normalize(other.Subtitle)
                && Normalize(book.Rights) == Normalize(other.Rights)
                && book.File.IsAbridged == other.File.IsAbridged
                && Normalize(book.Series?.Name) == Normalize(other.Series?.Name)
                && book.Series?.Index == other.Series?.Index
                && book.Authors.Select(a => a.Name).Order().SequenceEqual(other.Authors.Select(a => a.Name).Order(), StringComparer.Ordinal)
                && book.Contributors.Select(c => (c.Name, Role: Normalize(c.Role))).OrderBy(c => c.Name).SequenceEqual(other.Contributors.Select(c => (c.Name, Role: Normalize(c.Role))).OrderBy(c => c.Name))
                && book.Subjects.Order().SequenceEqual(other.Subjects.Order())
                && book.Identifiers.Distinct().Order().SequenceEqual(other.Identifiers.Distinct().Order());
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Shared formula for the deterministic cover image UUID — keeps the Book-based and key-based paths in lockstep.</summary>
    public static Guid CoverImageUuid(Guid bookUuid, DateTimeOffset modifiedAt) => Guid.GenerateUuidV5($"cover:{bookUuid}:{modifiedAt.ToUnixTimeSeconds()}");
}
