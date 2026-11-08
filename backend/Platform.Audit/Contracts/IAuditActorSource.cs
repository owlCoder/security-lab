namespace Platform.Audit.Contracts;

/// <summary>
/// Host hook that tells the Audit module who the caller of the CURRENT request is, so that the
/// ordinary <see cref="IAuditWriter"/> can work without the caller passing an actor.
/// The implementation must read only server-validated identity (a validated session), never an
/// HTTP body, query string or arbitrary header. Return <c>null</c> when there is no request
/// context at all; return <see cref="AuditActor.Anonymous"/> for a request without a confirmed
/// identity. See docs/components/audit/CONTRACTS.md.
/// </summary>
public interface IAuditActorSource
{
    AuditActor? GetCurrentActor();
}
