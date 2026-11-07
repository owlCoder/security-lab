// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-11: replaces the session identifier after a login or a change of the privilege level.
/// After a successful rotation the previous identifier is rejected.
/// </summary>
public interface IRotateSessionUseCase
{
    ValueTask<SessionRotationResult> RotateAsync(
        RotateSessionRequest? request,
        CancellationToken cancellationToken = default);
}
