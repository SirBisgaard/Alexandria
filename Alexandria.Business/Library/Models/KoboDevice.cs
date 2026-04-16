namespace Alexandria.Business.Library.Models;

/// <summary>
/// A registered Kobo device. Devices are pre-registered via the UI and authenticated by device key in the URL.
/// </summary>
public class KoboDevice
{
    public Guid DeviceUuid { get; set; }

    /// <summary>User-assigned display name for the device.</summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>Hardware serial number. Detected during the device's auth handshake.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>When locked, the device key cannot be used for sync until unlocked.</summary>
    public bool IsLocked { get; set; }

    /// <summary>Human-readable model name (e.g. "Kobo Libra Colour"). Detected during auth.</summary>
    public string? ModelName { get; set; }

    /// <summary>Kobo internal model code. Detected during auth.</summary>
    public string? ModelCode { get; set; }

    /// <summary>Device firmware version. Detected during auth.</summary>
    public string? Firmware { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>Timestamp of the device's most recent library sync request.</summary>
    public DateTimeOffset LastSyncedAt { get; set; }
}
