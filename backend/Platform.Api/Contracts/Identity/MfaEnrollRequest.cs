namespace Platform.Api.Contracts.Identity;

/// <summary>IDN-06 (#252): the current password of the session's account, checked before enrollment starts.</summary>
public sealed record MfaEnrollRequest(string? Password);
