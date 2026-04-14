namespace Alexandria.Api.Models.Requests;

public record CreateDeviceRequest(string Nickname, Guid? DeviceUuid = null) { }
