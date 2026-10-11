namespace Alexandria.Business.Library.Models;

/// <summary>
/// Screen a comic rendition is built for. Kobo models sharing a resolution and colour capability
/// share one profile, so a single rendition serves all of them. Resolutions are the panel's
/// portrait pixel size; colour models are Kaleido screens whose colour layer only applies to
/// pages that keep their colour.
/// </summary>
public sealed record ComicDeviceProfile
{
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsColor { get; init; }

    /// <summary>Folder name for this profile's renditions, e.g. <c>1264x1680-color</c>.</summary>
    public string Key => $"{Width}x{Height}-{(IsColor ? "color" : "gray")}";

    private static readonly ComicDeviceProfile Mini = new() { Width = 600, Height = 800 };
    private static readonly ComicDeviceProfile Nia = new() { Width = 758, Height = 1024 };
    private static readonly ComicDeviceProfile AuraH2O = new() { Width = 1080, Height = 1430 };
    private static readonly ComicDeviceProfile Clara = new() { Width = 1072, Height = 1448 };
    private static readonly ComicDeviceProfile ClaraColour = new() { Width = 1072, Height = 1448, IsColor = true };
    private static readonly ComicDeviceProfile Libra = new() { Width = 1264, Height = 1680 };
    private static readonly ComicDeviceProfile LibraColour = new() { Width = 1264, Height = 1680, IsColor = true };
    private static readonly ComicDeviceProfile Elipsa = new() { Width = 1404, Height = 1872 };
    private static readonly ComicDeviceProfile Forma = new() { Width = 1440, Height = 1920 };

    /// <summary>Keyed by the four-digit model code from the device's <c>Kobo Touch NNNN/</c> user agent.</summary>
    private static readonly Dictionary<string, ComicDeviceProfile> ByModelCode = new()
    {
        ["0310"] = Mini,
        ["0320"] = Clara,
        ["0330"] = Mini,
        ["0340"] = Elipsa,
        ["0350"] = AuraH2O,
        ["0360"] = Clara,
        ["0370"] = Forma,
        ["0371"] = Forma,
        ["0373"] = Elipsa,
        ["0375"] = Forma,
        ["0376"] = Libra,
        ["0377"] = Clara,
        ["0381"] = Nia,
        ["0383"] = Elipsa,
        ["0384"] = Clara,
        ["0386"] = ClaraColour,
        ["0387"] = LibraColour,
        ["0390"] = LibraColour
    };

    /// <summary>Null when the model is unknown or the device has not completed its first handshake.</summary>
    public static ComicDeviceProfile? ForModelCode(string? modelCode) => modelCode is null ? null : ByModelCode.GetValueOrDefault(modelCode);
}
