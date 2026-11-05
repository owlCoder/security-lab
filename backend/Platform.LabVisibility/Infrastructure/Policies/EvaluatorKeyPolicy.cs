using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Contracts;

namespace Platform.LabVisibility.Infrastructure.Policies;

// lab.key.read: ključ sme da čita samo evaluator i nastavnik kursa te laboratorije.
// Student nikad, ni kad je upisan na kurs (AUT-04).
public sealed class EvaluatorKeyPolicy(
    ILabPublicContentReader labs,
    ICourseMembership membership) : IAccessPolicy
{
    public string Id => "lab-visibility.evaluator-key";

    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Subject is null ||
            request.Resource is not { Type: "lab" } resource ||
            !Guid.TryParse(resource.Id, out var labId))
        {
            return false;
        }

        if (request.Subject.Id == EvaluatorPrincipal.Id)
        {
            return true;
        }

        if (!Guid.TryParse(request.Subject.Id, out var userId))
        {
            return false;
        }

        var lab = await labs.GetAsync(labId, cancellationToken);
        if (lab is null)
        {
            return false;
        }

        var role = await membership.GetRoleAsync(lab.CourseId, userId, cancellationToken);
        return role == CourseRole.Teacher;
    }
}
