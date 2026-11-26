using Npgsql;
using Platform.Access.Contracts;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Administration.Integration;

namespace Platform.Api.Modules.Administration.Persistence;

/// <summary>
/// Persistent ADM-08 operation boundary. Callers must supply a server-validated actor
/// and the shared Access decision engine. No sign-in or root-provisioning shortcut.
/// </summary>
public sealed class PersistentAdministrationService(string connectionString, IAccountReader accounts,
    IAccessDecision decisions, TimeProvider clock, ILogger<PersistentAdministrationService> logger,
    ITrustedCourseDirectory? courses = null)
{
    public async Task<OperationResult> Execute(Guid? actor, Guid course, string action, string correlation,
        RenameCourseRequest? rename = null, DelegateRequest? delegation = null,
        Guid? grantId = null, RevokeRequest? revoke = null, CancellationToken ct = default, Func<CancellationToken, Task<bool>>? validateSession = null)
    {
        ct.ThrowIfCancellationRequested();
        var unavailableStatus = actor is null || actor == Guid.Empty ? 401 : 404;

        async Task<OperationResult> RecordDenialAsync(int status, string reason)
        {
            await using var denied = await PostgresAdministrationSession.OpenAsync(connectionString, accounts, ct);
            await denied.WriteAsync(new(clock.GetUtcNow(), correlation, actor == Guid.Empty ? null : actor, action,
                grantId ?? course, course, "DENY", reason, "adm08:v1"), ct);
            await denied.CommitAsync(ct);
            return new(status);
        }

        try
        {
            if (courses is not null && await courses.FindAsync(course, ct) is not { Status: PortStatus.Ok, Value.Active: true })
                return await RecordDenialAsync(unavailableStatus, "COURSE_UNAVAILABLE");
            await using var session = await PostgresAdministrationSession.OpenAsync(connectionString, accounts, ct);
            var service = new AdministrationService(accounts, session, decisions, session, clock);
            var result = await service.Execute(actor, course, action, correlation, rename, delegation, grantId, revoke, ct);
            // A result is returned only AFTER both the change and its producer audit commit.
            ct.ThrowIfCancellationRequested();
            var sessionValid = validateSession is null || await validateSession(ct);
            var courseValid = courses is null || await courses.FindAsync(course, ct) is { Status: PortStatus.Ok, Value.Active: true };
            if (!sessionValid || !courseValid)
            {
                await session.RollbackAsync(ct);
                await session.DisposeAsync();
                // Preserve the rejection after rolling back the attempted change and its success event.
                return await RecordDenialAsync(sessionValid ? 404 : 401,
                    sessionValid ? "COURSE_UNAVAILABLE" : "SESSION_INVALIDATED");
            }
            await session.CommitAsync(ct);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is NpgsqlException or IOException or TimeoutException)
        {
            // No exception text/connection string, user input, answer or credential in logs.
            logger.LogWarning("ADM-08 transaction unavailable. CorrelationId: {CorrelationId}", correlation);
            return new(unavailableStatus);
        }
    }
}
