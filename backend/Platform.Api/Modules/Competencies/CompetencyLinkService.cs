using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Competencies.Contracts;
using Platform.Competencies.Domain;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using AccessDecision = Platform.Access.Contracts.IAccessDecision;
using CourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.Api.Modules.Competencies;

// Call only with the user ID produced by Prg04SessionIdentity (not an HTTP field).
// All reads/writes of competency links happen after the concrete laboratory guard.
public sealed class CompetencyLinkService(
    ILaboratoryRepository laboratories,
    ICourseMembership memberships,
    ILabPublicContentReader publicContent,
    AccessDecision access,
    IServiceProvider services)
{
    public async Task<CompetencyOperationResult> CatalogAsync(CancellationToken ct)
    {
        var catalog = services.GetService<ICompetencyCatalog>();
        return catalog is null
            ? new(CompetencyOperationStatus.StorageUnavailable)
            : new(CompetencyOperationStatus.Ok, await catalog.ListAsync(ct));
    }

    public async Task<CompetencyOperationResult> ReadAsync(Guid userId, Guid laboratoryId, CancellationToken ct)
    {
        var guard = await GuardAsync(userId, laboratoryId, edit: false, ct);
        if (guard.Status != CompetencyOperationStatus.Ok) return guard;
        var store = services.GetService<ILaboratoryCompetencyStore>();
        return store is null
            ? new(CompetencyOperationStatus.StorageUnavailable)
            : new(CompetencyOperationStatus.Ok, await store.GetForLaboratoryAsync(laboratoryId, ct),
                guard.LaboratoryTitle, await CanManageAsync(userId, laboratoryId, ct));
    }

    public async Task<CompetencyOperationResult> ChangeAsync(
        Guid userId, Guid laboratoryId, Guid competencyId, bool add, CancellationToken ct)
    {
        var guard = await GuardAsync(userId, laboratoryId, edit: true, ct);
        if (guard.Status != CompetencyOperationStatus.Ok) return guard;
        if (competencyId == Guid.Empty)
            return new(CompetencyOperationStatus.InvalidCompetencyId);

        var catalog = services.GetService<ICompetencyCatalog>();
        var store = services.GetService<ILaboratoryCompetencyStore>();
        if (catalog is null || store is null)
            return new(CompetencyOperationStatus.StorageUnavailable);

        // The catalog is deliberately tiny and immutable in this PRG-04 increment.
        // Reuse its existing contract; no change to its interface or other consumers.
        if (!(await catalog.ListAsync(ct)).Any(x => x.Id == competencyId))
            return new(CompetencyOperationStatus.CompetencyNotFound);

        if (!add)
        {
            await store.RemoveAsync(laboratoryId, competencyId, ct);
            return new(CompetencyOperationStatus.NoContent);
        }

        try
        {
            bool created = await store.AddAsync(laboratoryId, competencyId, ct);
            return new(created ? CompetencyOperationStatus.Created : CompetencyOperationStatus.NoContent);
        }
        catch (KeyNotFoundException)
        {
            return new(CompetencyOperationStatus.CompetencyNotFound);
        }
    }

    private async Task<CompetencyOperationResult> GuardAsync(
        Guid userId, Guid laboratoryId, bool edit, CancellationToken ct)
    {
        // Never grant anonymous/internal invalid identities access, even if this service
        // is accidentally called without its HTTP identity guard.
        if (userId == Guid.Empty)
            return new(CompetencyOperationStatus.LaboratoryNotFound);
        if (laboratoryId == Guid.Empty)
            return new(CompetencyOperationStatus.InvalidLaboratoryId);

        var laboratory = await laboratories.GetByIdAsync(laboratoryId, ct);
        if (laboratory is null || laboratory.Id != laboratoryId || laboratory.CourseId == Guid.Empty)
            return new(CompetencyOperationStatus.LaboratoryNotFound);

        if (!edit)
        {
            var role = await memberships.GetRoleAsync(laboratory.CourseId, userId, ct);
            if (!LabOpenPolicy.DecideOpen(laboratory.CourseId, role,
                laboratory.CreatedBy == userId).Allowed)
                return new(CompetencyOperationStatus.LaboratoryNotFound);

            // Course membership alone must not expose links belonging to a draft
            // or withdrawn laboratory. Publication belongs to the visibility module.
            if (role == CourseRole.Student)
            {
                if (laboratory.Status == LabStatus.Archived)
                    return new(CompetencyOperationStatus.LaboratoryNotFound);
                var content = await publicContent.GetAsync(laboratoryId, ct);
                if (content is null || content.LabId != laboratoryId ||
                    content.CourseId != laboratory.CourseId || content.State != LabVisibilityState.Published)
                    return new(CompetencyOperationStatus.LaboratoryNotFound);
            }

            return new(CompetencyOperationStatus.Ok, LaboratoryTitle: laboratory.Title);
        }

        // Read access is deliberately NOT reused as permission to edit.
        return await CanManageAsync(userId, laboratoryId, ct)
            ? new(CompetencyOperationStatus.Ok, LaboratoryTitle: laboratory.Title)
            : new(CompetencyOperationStatus.LaboratoryNotFound);
    }

    // The GET capability and every mutation use the same ACC policy. A capability
    // describes the current request; it never replaces the next write's check.
    private async Task<bool> CanManageAsync(Guid userId, Guid laboratoryId, CancellationToken ct)
    {
        var decision = await access.DecideAsync(new AccessRequest(
            new SubjectRef(userId.ToString("D"), AccountState.Active),
            CompetencyLinkActions.Manage,
            new ResourceRef("laboratory", laboratoryId.ToString("D")),
            Guid.NewGuid().ToString("N")), ct);
        return decision.Outcome == AccessOutcome.Allow;
    }
}

