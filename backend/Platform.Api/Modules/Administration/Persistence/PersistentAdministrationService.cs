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
        Guid? grantId = null, RevokeRequest? revoke = null, CancellationToken ct = default, Func<CancellationToken, Task<bool>>? validateSession = null)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await using var session = await PostgresAdministrationSession.OpenAsync(connectionString, accounts, ct);
            var service = new AdministrationService(accounts, session, decisions, session, clock);
            var result = await service.Execute(actor, course, action, correlation, rename, delegation, grantId, revoke, ct);
            // A result is returned only AFTER both the change and its producer audit commit.
            ct.ThrowIfCancellationRequested();
            if (validateSession is not null && !await validateSession(ct))
            {
                await session.RollbackAsync(ct);
                await session.DisposeAsync();
                // Preserve the rejection after rolling back the attempted change and its success event.
                await using var denied = await PostgresAdministrationSession.OpenAsync(connectionString, accounts, ct);
                await denied.WriteAsync(new(clock.GetUtcNow(), correlation, actor, action, grantId ?? course, course,
                    "DENY", "SESSION_INVALIDATED", "adm08:v1"), ct);
                await denied.CommitAsync(ct);
                return new(401);
            }
            await session.CommitAsync(ct);
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
