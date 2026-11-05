namespace Platform.Api.Models.Identity;

public sealed record LoginRequest(string? Email, string? Password);
