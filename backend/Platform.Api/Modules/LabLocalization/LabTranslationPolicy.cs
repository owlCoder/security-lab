using Microsoft.EntityFrameworkCore;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Laboratories.Infrastructure.Persistence;
using Platform.LabVisibility.Contracts;
using LabCourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.Api.Modules.LabLocalization;

// Prevod dodaje nastavnik kursa koji je autor laboratorije kojoj revizija pripada (isto pravilo kao PRG-04).
public sealed class LabTranslationPolicy(
    IServiceScopeFactory scopes,
    IDbContextFactory<LaboratoriesDbContext> contexts) : IAccessPolicy
{
    public string Id => LabLocalizationActions.Translate;
    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Action != LabLocalizationActions.Translate ||
            request.Subject is not { State: AccountState.Active } subject ||
            !Guid.TryParse(subject.Id, out var userId) || userId == Guid.Empty ||
            request.Resource is not { Type: LabLocalizationActions.RevisionResource } resource ||
            !Guid.TryParse(resource.Id, out var revisionId) || revisionId == Guid.Empty)
        {
            return false;
        }

        Guid laboratoryId;
        await using (var db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            laboratoryId = await db.ReviewDefinitions.AsNoTracking()
                .Where(value => value.RevisionId == revisionId)
                .Select(value => value.LaboratoryId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (laboratoryId == Guid.Empty)
        {
            return false;
        }

        await using var scope = scopes.CreateAsyncScope();
        var laboratory = await scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>()
            .GetByIdAsync(laboratoryId, cancellationToken);
        if (laboratory is null || laboratory.Id != laboratoryId ||
            laboratory.CourseId == Guid.Empty || laboratory.CreatedBy != userId)
        {
            return false;
        }

        LabCourseRole role = await scope.ServiceProvider.GetRequiredService<ICourseMembership>()
            .GetRoleAsync(laboratory.CourseId, userId, cancellationToken);
        return role == LabCourseRole.Teacher;
    }
}
