namespace Alexandria.Business.Library.Models.Settings;

/// <summary>
/// Controls how M4B audiobook files are processed during library indexing.
/// </summary>
public enum M4BFileTreatment
{
    /// <summary>
    /// Stream M4B chapters on demand. Chapters are transcoded to MP3 in real-time when the Kobo requests them. Long chapters risk timeout.
    /// </summary>
    Nothing,

    /// <summary>
    /// Pre-transcode each chapter to MP3 during library indexing. Ensures transfers complete at the cost of additional disk space.
    /// </summary>
    TranscodeAtIndex,

    /// <summary>
    /// Convert the entire M4B into an MP3 folder during indexing and delete the original M4B file when done. Best compatibility with Kobo devices and uses the same disk space.
    /// </summary>
    ConvertAtIndex
}
