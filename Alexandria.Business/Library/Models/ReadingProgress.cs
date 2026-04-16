namespace Alexandria.Business.Library.Models;

/// <summary>
/// Reading state reported by a Kobo device via the sync protocol.
/// </summary>
public class ReadingProgress
{
    public ReadingStatus Status { get; set; } = ReadingStatus.ReadyToRead;

    /// <summary>Percentage of the book completed (0–100). Reported by the Kobo device.</summary>
    public decimal? ProgressPercent { get; set; }

    /// <summary>Total time spent reading/listening in minutes. Reported by the Kobo device.</summary>
    public int? SpentReadingMinutes { get; set; }

    /// <summary>Estimated remaining time in minutes. Reported by the Kobo device.</summary>
    public int? RemainingTimeMinutes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}
