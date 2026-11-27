using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Administration.Integration;

namespace Platform.Api.Modules.Administration.Persistence;

public sealed record AdministrationDatabase(string ConnectionString)
{
    public override string ToString() => "AdministrationDatabase (redacted)";
}

/// <summary>A singleton policy opens a fresh, short-lived database session per decision.</summary>
public sealed class PersistentAdministrationPolicy(AdministrationDatabase database, IAccountReader accounts,
    ITrustedCourseDirectory? courses = null) : IAccessPolicy
{
    public string Id => "adm08.persisted.scoped.delegation";
    public string Version => "v1"; // central Access audit metadata format

    public async ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (courses is not null && (request.Resource?.Type != "course" || !Guid.TryParse(request.Resource.Id, out var id) ||
            await courses.FindAsync(id, cancellationToken) is not { Status: PortStatus.Ok, Value.Active: true } trusted || trusted.Value.Id != id))
            return false;
        await using var session = await PostgresAdministrationSession.OpenAsync(database.ConnectionString, accounts, cancellationToken);
        return await new AdministrationPolicy(accounts, session).EvaluateAsync(request, cancellationToken);
    }
}
