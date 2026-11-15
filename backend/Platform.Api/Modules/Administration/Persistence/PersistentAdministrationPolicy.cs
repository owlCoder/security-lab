using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Access;

namespace Platform.Api.Modules.Administration.Persistence;

public sealed record AdministrationDatabase(string ConnectionString)
{
    public override string ToString() => "AdministrationDatabase (redacted)";
}

/// <summary>A singleton policy opens a fresh, short-lived database session per decision.</summary>
public sealed class PersistentAdministrationPolicy(AdministrationDatabase database, IAccountReader accounts) : IAccessPolicy
{
    public string Id => "adm08.persisted.scoped.delegation";
    public string Version => "adm08:postgres-v1";

    public async ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = await PostgresAdministrationSession.OpenAsync(database.ConnectionString, accounts, cancellationToken);
        return await new AdministrationPolicy(accounts, session).EvaluateAsync(request, cancellationToken);
    }
}
