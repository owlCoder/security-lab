using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Access;

namespace Platform.Api.Modules.Administration;

public sealed class AdministrationPolicy(IAccountReader accounts, ICourseAdministrationStore courses) : IAccessPolicy
{
    public const string Read = "course.admin.read";
    public const string Rename = "course.settings.update";
    public const string Delegate = "course.delegation.create";
    public const string Revoke = "course.delegation.revoke";
    public string Id => "adm08.scoped.delegation";
    public string Version => "adm08:v1";
    public static AdminCapability Required(string action) => action switch
    {
        Read => AdminCapability.ViewAdministration,
        Rename => AdminCapability.ManageSettings,
        Delegate or Revoke => AdminCapability.ManageDelegations,
        _ => AdminCapability.None
    };
    public async ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Subject is null || !Guid.TryParse(request.Subject.Id, out var actor) ||
            request.Resource?.Type != "course" || !Guid.TryParse(request.Resource.Id, out var course) ||
            actor == Guid.Empty || course == Guid.Empty || Required(request.Action) == AdminCapability.None)
            return false;
        var account = await accounts.FindAccountAsync(actor, cancellationToken);
        if (account.Status != PortStatus.Ok || account.Value is not { IsActive: true } || account.Value.Id != actor)
            return false;
        var c = await courses.FindCourseAsync(course, cancellationToken);
        var grant = await courses.FindAuthorityAsync(actor, course, cancellationToken);
        return c.Status == PortStatus.Ok && c.Value?.Id == course &&
            grant.Status == PortStatus.Ok && grant.Value is not null && Valid(grant.Value, actor, course, Required(request.Action));
    }
    public static bool Valid(AdminGrant grant, Guid actor, Guid course, AdminCapability required) =>
        grant.Id != Guid.Empty && grant.GranteeId == actor && grant.CourseId == course &&
        grant.State == GrantState.Active && grant.Version > 0 &&
        (grant.Capabilities & required) == required &&
        (grant.Capabilities & ~(AdminCapability.ViewAdministration | AdminCapability.ManageSettings | AdminCapability.ManageDelegations)) == 0 &&
        (required != AdminCapability.ManageDelegations || grant.IsRoot);
}
