namespace Alexandria.Business.Library.Models;

/// <summary>
/// Reading state lifecycle. Serialized as strings in JSON (via JsonStringEnumConverter).
/// The Kobo protocol layer uses raw string conversion — not these enum values directly.
/// </summary>
public enum ReadingStatus
{
    ReadyToRead,
    Reading,
    Finished
}
