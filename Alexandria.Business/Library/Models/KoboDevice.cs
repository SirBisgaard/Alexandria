namespace Alexandria.Business.Library.Models;

public class KoboDevice
{
    public Guid DeviceUuid { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string? DeviceToken { get; set; }
    public string? ModelName { get; set; }
    public string? ModelCode { get; set; }
    public string? Firmware { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public DateTimeOffset LastRequestAt { get; set; }
}
