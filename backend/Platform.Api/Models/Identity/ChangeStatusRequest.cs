namespace Platform.Api.Models.Identity;

public sealed record ChangeStatusRequest(string? Status, string? Reason);
