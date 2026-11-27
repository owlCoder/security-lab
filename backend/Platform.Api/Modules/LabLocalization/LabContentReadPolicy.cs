using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;
using Platform.LabVisibility.Contracts;
using LabCourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.Api.Modules.LabLocalization;

// Sadržaj sme da čita onaj ko sme da otvori laboratoriju (LAB-03): student kursa ili nastavnik kursa koji je autor.
public sealed class LabContentReadPolicy(IServiceScopeFactory scopes) : IAccessPolicy
{
    public string Id => LabLocalizationActions.Read;
    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Action != LabLocalizationActions.Read ||
            request.Subject is not { State: AccountState.Active } subject ||
            !Guid.TryParse(subject.Id, out var userId) || userId == Guid.Empty ||
            request.Resource is not { Type: LabLocalizationActions.LaboratoryResource } resource ||
            !Guid.TryParse(resource.Id, out var laboratoryId) || laboratoryId == Guid.Empty)
        {
            return false;
        }

        // Politika je singleton, a repozitorijum laboratorija može biti scoped.
        await using var scope = scopes.CreateAsyncScope();
        var laboratory = await scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>()
            .GetByIdAsync(laboratoryId, cancellationToken);
        if (laboratory is null || laboratory.Id != laboratoryId)
        {
            return false;
        }

        LabCourseRole role = laboratory.CourseId == Guid.Empty
            ? LabCourseRole.None
            : await scope.ServiceProvider.GetRequiredService<ICourseMembership>()
                .GetRoleAsync(laboratory.CourseId, userId, cancellationToken);

        return LabOpenPolicy.DecideOpen(laboratory.CourseId, role, laboratory.CreatedBy == userId).Allowed;
    }
}
