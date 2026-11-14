using Platform.Api.Enums.Identity;

namespace Platform.Api.Interfaces.Identity;

public interface IMfaService
{
    Task<(MfaOutcome Outcome, string? Uri)> EnrollAsync(
        string? rawToken,
        string correlationId,
        CancellationToken ct);

    Task<MfaOutcome> ConfirmEnrollAsync(
        string? rawToken,
        string? code,
        string correlationId,
        CancellationToken ct);

    Task<MfaOutcome> VerifyAsync(
        string? rawToken,
        string? code,
        string correlationId,
        CancellationToken ct);
}