using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Audit;

/// <summary>AUD-10: placeholder audit writer used until the real adapter to the Audit module
/// (Team 12) is wired in. Always reports failure, so every rotation is fail-closed in the
/// meantime.</summary>
internal sealed class UnavailableSessionSecurityAuditWriter : ISessionSecurityAuditWriter
{
    public ValueTask<bool> TryWriteAsync(
        SessionSecurityAuditEntry entry,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
}
