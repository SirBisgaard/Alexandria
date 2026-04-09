using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Library.Extensions;

public static class BookExtensions
{
    extension(Book book)
    {
        /// <summary>Generates a deterministic UUID v5 from title, authors, and series.</summary>
        public Guid GenerateDeterministicUuid()
        {
            var title = book.Title.ToLowerInvariant().Trim();
            var authors = string.Join(',', book.Authors.Select(a => a.Name.ToLowerInvariant().Trim()));
            var series = book.Series?.Name?.ToLowerInvariant().Trim() + "#" + book.Series?.Index;

            var prefix = book.MediaType == MediaType.Ebook ? "" : $"{book.MediaType.ToString().ToLowerInvariant()}:";
            return Guid.GenerateUuidV5($"{prefix}{title}:{authors}:{series}");
        }

        /// <summary>Generates a format-agnostic UUID v5 from title and authors only, for linking ebook/audiobook editions of the same work.</summary>
        public Guid GenerateWorkUuid()
        {
            var title = book.Title.ToLowerInvariant().Trim();
            var authors = string.Join(',', book.Authors.Select(a => a.Name.ToLowerInvariant().Trim()));
            return Guid.GenerateUuidV5($"work:{title}:{authors}");
        }

        /// <summary>Compares managed (editable) metadata fields. Pass-through DC elements are excluded.</summary>
        public bool MetadataEquals(Book other)
        {
            return book.Title == other.Title
                && book.Language == other.Language
                && Normalize(book.PublicationDate) == Normalize(other.PublicationDate)
                && Normalize(book.Description) == Normalize(other.Description)
                && Normalize(book.Publisher) == Normalize(other.Publisher)
                && Normalize(book.Series?.Name) == Normalize(other.Series?.Name)
                && book.Series?.Index == other.Series?.Index
                && book.Authors.Select(a => a.Name).SequenceEqual(other.Authors.Select(a => a.Name), StringComparer.Ordinal);
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
