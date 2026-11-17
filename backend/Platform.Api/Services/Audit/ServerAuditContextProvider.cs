using Platform.Api.Interfaces.Identity;
using Platform.Audit.Application;
using Platform.Audit.Contracts;

namespace Platform.Api.Services.Audit;

/// <summary>
/// Builds Audit context exclusively from server-managed request state. The actor comes
/// from the session validated by SessionCheckMiddleware and the correlation ID comes
/// from CorrelationMiddleware; neither value is accepted from request input.
/// </summary>
internal sealed class ServerAuditContextProvider(
    IIdentityContext identity,
    ICorrelationContext correlation) : IAuditContextProvider
{
    public AuditContext? GetCurrentContext()
    {
        if (string.IsNullOrWhiteSpace(correlation.CorrelationId))
            return null;

        var caller = identity.Current;
        var actor = caller is { IsAuthenticated: true, UserId: Guid userId }
            ? AuditActor.ForUser(userId)
            : AuditActor.Anonymous;

        return new AuditContext(actor, correlation.CorrelationId);
    }
}
