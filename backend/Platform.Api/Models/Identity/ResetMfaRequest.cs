namespace Platform.Api.Models.Identity;

/// <summary>Body of POST /api/admin/accounts/{id}/mfa/reset. The reason is required.</summary>
public sealed record ResetMfaRequest(string? Reason);
