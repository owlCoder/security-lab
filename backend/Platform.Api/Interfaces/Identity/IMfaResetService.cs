using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

/// <summary>
/// US-32: an administrator removes the second factor of another account and all of its
/// sessions are revoked. See docs/contracts/IMfaResetService.md (v0.2).
/// </summary>
public interface IMfaResetService
{
    /// <param name="actorUserId">Always from the server-side identity context, never from the request.</param>
    /// <param name="reason">Required, max 200 characters, no control characters.</param>
    Task<MfaResetResult> ResetAsync(Guid actorUserId, Guid targetUserId, string reason, CancellationToken ct = default);
}
