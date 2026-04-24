namespace Alexandria.Business.Library.Models;

/// <summary>
/// Narrow projection of a book used to recompute cover image UUIDs without loading the full
/// book graph. Used by the Kobo cover route to map an incoming cover UUID back to a book.
/// </summary>
public sealed record BookCoverKey
{
    public Guid BookUuid { get; init; }
    public Guid EntitlementUuid { get; init; }
    public DateTimeOffset ModifiedAt { get; init; }
}
