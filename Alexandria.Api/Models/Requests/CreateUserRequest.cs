namespace Alexandria.Api.Models.Requests;

public record CreateUserRequest(string Username, string Password, string DisplayName) { }
