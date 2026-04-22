using System.ComponentModel.DataAnnotations;
using Alexandria.Business.Library.Models.Settings;

namespace Alexandria.Api.Models.Requests;

public record UpdateSettingsRequest
{
    [Range(1, 1000, ErrorMessage = "Batch size must be between 1 and 1000.")]
    public int SyncBatchSize { get; set; }

    public bool ProxyLoggingEnabled { get; set; }


    [Range(1, 1000, ErrorMessage = "Words per page must be between 1 and 1000.")]
    public int WordsPerPage { get; set; }

    public bool SyncLibraryAtBoot { get; set; }

    public M4BFileTreatment M4BFileTreatment { get; set; }

    [Required(ErrorMessage = "File rename template is required.")]
    [MinLength(1, ErrorMessage = "File rename template must not be empty.")]
    [MaxLength(500, ErrorMessage = "File rename template must be 500 characters or fewer.")]
    public string FileRenameTemplate { get; set; } = string.Empty;
}
