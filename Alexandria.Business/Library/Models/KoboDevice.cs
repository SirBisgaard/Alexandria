namespace Alexandria.Business.Library.Models;

public class KoboDevice
{
    public Guid DeviceUuid { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public bool IsLocked { get; set; }
    public string? ModelName { get; set; }
    public string? ModelCode { get; set; }
    public string? Firmware { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}
