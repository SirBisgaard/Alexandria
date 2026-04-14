namespace Alexandria.Api.Models.Requests;

public record UpdateUserRequest(string Username, string? Password, string DisplayName) { }
