using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Modules.Access;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;

namespace Platform.Api.Modules.Administration.Api;

/// <summary>Fresh Identity scopes, including repeated checks within a single request.</summary>
public sealed class IdentityAdministrationAccounts(IServiceScopeFactory scopes,
    ILogger<IdentityAdministrationAccounts> logger) : IAccountReader
{
    public async ValueTask<PortResult<AccountSnapshot>> FindAccountAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) return PortResult<AccountSnapshot>.Missing();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var account = await scope.ServiceProvider.GetRequiredService<IAccountStatusReader>().FindAsync(id, ct);
            return account is not null && account.Id == id
                ? PortResult<AccountSnapshot>.Found(new(id, account.Status == AccountStatus.Active))
                : PortResult<AccountSnapshot>.Missing();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is NpgsqlException or DbUpdateException or IOException or TimeoutException)
        {
            logger.LogWarning("ADM-08 account provider unavailable.");
            return PortResult<AccountSnapshot>.Offline();
        }
    }
}

public sealed class AdministrationSessions(IServiceScopeFactory scopes)
{
    public async Task<ValidateSession?> ValidateAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512) return null;
        await using var scope = scopes.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<ISessionManager>().ValidateAsync(token, ct);
        return session is { Level: AuthenticationLevel.Active } && session.UserId != Guid.Empty ? session : null;
    }
}
