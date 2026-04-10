using System.ComponentModel.DataAnnotations;

namespace Alexandria.Api.Models.Requests;

public record UpdateSettingsRequest
{
    [Range(1, 1000, ErrorMessage = "Batch size must be between 1 and 1000.")]
    public int SyncBatchSize { get; set; }

    public bool ProxyLoggingEnabled { get; set; }


    [Range(1, 1000, ErrorMessage = "Words per page must be between 1 and 1000.")]
    public int WordsPerPage { get; set; }
}
