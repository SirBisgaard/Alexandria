namespace Alexandria.Business.Library.Models.Settings;

/// <summary>
/// User-facing choices for turning comics into fixed-layout EPUBs, modelled on Kindle Comic
/// Converter's (KCC) options and edited on the Settings page. Stored as JSON in
/// <c>Settings.ComicRendering</c>; keys missing from the stored JSON take these defaults. Every
/// value is part of each rendition's signature, so changing any of them rebuilds all comic
/// renditions on the next library sync. Detection thresholds are not user settings — they live
/// in the builder and are covered by its version number.
/// </summary>
public sealed record ComicRenderingSettings
{
    /// <summary>What happens to double-page spreads (KCC's "Spread splitter").</summary>
    public ComicSpreadHandling SpreadHandling { get; init; } = ComicSpreadHandling.Rotate;

    /// <summary>Whether empty page margins are cropped away (KCC's "Cropping mode").</summary>
    public ComicMarginCropping MarginCropping { get; init; } = ComicMarginCropping.Margins;

    /// <summary>How eagerly margins are cropped (KCC's "Cropping power").</summary>
    public ComicCroppingStrength CroppingStrength { get; init; } = ComicCroppingStrength.Normal;

    /// <summary>Percentage of the page kept as margin around the detected art (KCC's "Preserve margin %").</summary>
    public double PreserveMarginPercent { get; init; } = 1.0;

    /// <summary>JPEG quality (1–100) of rendered pages. KCC's default for Kobo profiles.</summary>
    public int JpegQuality { get; init; } = 85;

    /// <summary>Which pages are auto-levelled (KCC's "Custom autocontrast").</summary>
    public ComicAutoContrast AutoContrast { get; init; } = ComicAutoContrast.BlackAndWhiteOnly;
}

public enum ComicSpreadHandling
{
    /// <summary>Rotate the spread a quarter turn so its right edge points up and it fills the portrait screen.</summary>
    Rotate,

    /// <summary>Cut the spread into its two pages, ordered by reading direction. Very wide panoramas are rotated instead.</summary>
    Split,

    /// <summary>Show the spread twice: first as its two split pages, then rotated whole.</summary>
    SplitAndRotate
}

public enum ComicMarginCropping
{
    Off,

    /// <summary>Crop empty margins around the art.</summary>
    Margins
}

public enum ComicCroppingStrength
{
    /// <summary>Only crops plain, perfectly even margins; anything faint counts as art.</summary>
    Gentle,

    Normal,

    /// <summary>Also crops margins with some texture or faint marks.</summary>
    Aggressive
}

public enum ComicAutoContrast
{
    /// <summary>Auto-level grayscale pages only.</summary>
    BlackAndWhiteOnly,

    Off,

    /// <summary>Auto-level grayscale and colour pages.</summary>
    All
}
