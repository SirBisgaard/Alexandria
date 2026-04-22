namespace Alexandria.Api.Models.Requests;

public record CreateDeviceRequest
{
    public string Nickname { get; init; } = string.Empty;
    public Guid? DeviceUuid { get; init; }
}
