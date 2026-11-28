using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Api.Services.Identity;

/// <summary>
/// Default IMfaResetService until the real MFA service (Marijana, US-19/US-32) is registered.
/// It changes nothing and answers Unavailable, so the endpoint returns 503 instead of failing at startup.
/// </summary>
public sealed class UnavailableMfaResetService : IMfaResetService
{
    public Task<MfaResetResult> ResetAsync(Guid actorUserId, Guid targetUserId, string reason, CancellationToken ct = default) =>
        Task.FromResult(new MfaResetResult(MfaResetOutcome.Unavailable));
}
