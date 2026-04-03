namespace Alexandria.Business.Models.KoboStore;

/// <summary>
/// A sync result item that pushes updated reading state to a Kobo device.
/// Kobo devices ignore ReadingState embedded in ChangedEntitlement for books they already have locally.
/// A separate ChangedReadingState sync item is the only way to update reading state on existing books.
/// </summary>
public record ChangedReadingState
{
    public required ReadingState ReadingState { get; set; }
}