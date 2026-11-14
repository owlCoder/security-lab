using Npgsql;
using Platform.Access.Contracts;
using Platform.Api.Modules.Access;

namespace Platform.Api.Modules.Administration.Persistence;

/// <summary>
/// Persistent ADM-08 operation boundary. Callers must supply a server-validated actor
/// and the shared Access decision engine. No sign-in or root-provisioning shortcut.
/// </summary>
public sealed class PersistentAdministrationService(string connectionString, IAccountReader accounts,
    IAccessDecision decisions, TimeProvider clock, ILogger<PersistentAdministrationService> logger)
{
    public async Task<OperationResult> Execute(Guid? actor, Guid course, string action, string correlation,
        RenameCourseRequest? rename = null, DelegateRequest? delegation = null,
        Guid? grantId = null, RevokeRequest? revoke = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var session = new PostgresAdministrationSession(connectionString, accounts);
            var service = new AdministrationService(accounts, session, decisions, session, clock);
            var result = await service.Execute(actor, course, action, correlation, rename, delegation, grantId, revoke, ct);
            // A result is returned only AFTER both the change and its producer audit commit.
            ct.ThrowIfCancellationRequested();
            session.Commit();
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is NpgsqlException or IOException or TimeoutException)
        {
            // No exception text/connection string, user input, answer or credential in logs.
            logger.LogWarning("ADM-08 transaction unavailable. CorrelationId: {CorrelationId}", correlation);
            return new(actor is null || actor == Guid.Empty ? 401 : 404);
        }
    }
}
