using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Contracts;

namespace Platform.LabVisibility.Infrastructure.Policies;

// AUT-07: pregled pre objave sme samo nastavnik kursa kome laboratorija pripada.
// Student nikad, ni kad je upisan na kurs.
// Aktivan nalog i postojanje radnje već proverava IAccessDecision tima 3 pre ove politike.
public sealed class LabPreviewPolicy(
    ILabPublicContentReader reader,
    ICourseMembership membership) : IAccessPolicy
{
    public string Id => "lab.preview.course-teacher-only";
    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Subject is null || request.Resource is null) return false;
        if (request.Resource.Type != "lab") return false;
        if (!Guid.TryParse(request.Resource.Id, out var labId)) return false;
        if (!Guid.TryParse(request.Subject.Id, out var userId)) return false;

        // Nepostojeća laboratorija = odbijeno (kasnije isti 404 kao zabranjeno).
        var lab = await reader.GetAsync(labId, cancellationToken);
        if (lab is null) return false;

        var role = await membership.GetRoleAsync(lab.CourseId, userId, cancellationToken);
        return role == CourseRole.Teacher;
    }
}
