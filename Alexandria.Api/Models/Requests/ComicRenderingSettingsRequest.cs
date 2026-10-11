using System.ComponentModel.DataAnnotations;
using Alexandria.Business.Library.Models.Settings;

namespace Alexandria.Api.Models.Requests;

public record ComicRenderingSettingsRequest
{
    public ComicSpreadHandling SpreadHandling { get; set; }

    public ComicMarginCropping MarginCropping { get; set; }

    public ComicCroppingStrength CroppingStrength { get; set; }

    [Range(0.0, 10.0, ErrorMessage = "Preserved margin must be between 0 and 10 %.")]
    public double PreserveMarginPercent { get; set; }

    [Range(1, 100, ErrorMessage = "JPEG quality must be between 1 and 100.")]
    public int JpegQuality { get; set; }

    public ComicAutoContrast AutoContrast { get; set; }
}
