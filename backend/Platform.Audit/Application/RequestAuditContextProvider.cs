using Platform.Audit.Contracts;

namespace Platform.Audit.Application;

/// <summary>
/// Gives the ordinary <see cref="IAuditWriter"/> its trusted context: the actor comes from the
/// host's <see cref="IAuditActorSource"/> (validated session), the correlation ID from the
/// server-generated request context. If either is missing the writer rejects the call.
/// </summary>
internal sealed class RequestAuditContextProvider(
    IAuditActorSource actors,
    ICorrelationContext correlation) : IAuditContextProvider
{
    public AuditContext? GetCurrentContext()
    {
        var actor = actors.GetCurrentActor();
        var correlationId = correlation.CorrelationId;

        return actor is null || string.IsNullOrWhiteSpace(correlationId)
            ? null
            : new AuditContext(actor, correlationId);
    }
}
