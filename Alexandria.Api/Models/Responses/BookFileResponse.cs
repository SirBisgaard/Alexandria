using Alexandria.Business.Library.Models;

namespace Alexandria.Api.Models.Responses;

public class BookFileResponse
{
    public MediaType MediaType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public bool HasCover { get; set; }
    public long TotalFileSize { get; set; }
    public int? PageCount { get; set; }
    public int? WordCount { get; set; }
    public int? Duration { get; set; }
    public bool? IsAbridged { get; set; }
    public string? AudioCodec { get; set; }
    public int? AudioBitrate { get; set; }
}
