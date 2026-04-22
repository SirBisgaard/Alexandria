namespace Alexandria.Api.Models.Requests;

public record UpdateDeviceRequest
{
    public string Nickname { get; init; } = string.Empty;
}
