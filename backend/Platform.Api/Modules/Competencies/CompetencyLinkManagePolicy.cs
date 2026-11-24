using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;
using Platform.LabVisibility.Contracts;

namespace Platform.Api.Modules.Competencies;

// AddAccessAction requires singleton policies. Resolve scoped repositories inside
// a fresh async scope; never capture a DbContext in this singleton.
public sealed class CompetencyLinkManagePolicy(IServiceScopeFactory scopes) : IAccessPolicy
{
    public string Id => "prg04.competency-links.manage";
    public string Version => "1";

    public async ValueTask<bool> EvaluateAsync(AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Action != CompetencyLinkActions.Manage ||
            request.Subject is not { State: AccountState.Active } subject ||
            !Guid.TryParse(subject.Id, out var userId) || userId == Guid.Empty ||
            request.Resource is not { Type: "laboratory" } resource ||
            !Guid.TryParse(resource.Id, out var laboratoryId) || laboratoryId == Guid.Empty)
            return false;

        await using var scope = scopes.CreateAsyncScope();
        var laboratories = scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>();
        var laboratory = await laboratories.GetByIdAsync(laboratoryId, cancellationToken);
        if (laboratory is null || laboratory.Id != laboratoryId ||
            laboratory.CourseId == Guid.Empty || laboratory.CreatedBy != userId)
            return false;

        var membership = scope.ServiceProvider.GetRequiredService<ICourseMembership>();
        var role = await membership.GetRoleAsync(laboratory.CourseId, userId, cancellationToken);
        return role == Platform.LabVisibility.Contracts.CourseRole.Teacher;
    }
}

