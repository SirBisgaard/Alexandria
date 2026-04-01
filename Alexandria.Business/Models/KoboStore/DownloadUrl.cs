namespace Alexandria.Business.Models.KoboStore;

public record DownloadUrl
{
    public required string Format { get; set; }  // "KEPUB", "EPUB", "EPUB3", "EPUB3FL"
    public required long Size { get; set; }
    public required string Url { get; set; }
    public string Platform { get; set; } = "Generic";
}