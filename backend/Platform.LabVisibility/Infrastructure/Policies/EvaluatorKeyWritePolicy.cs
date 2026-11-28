using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Infrastructure.Policies;

public sealed class EvaluatorKeyWritePolicy(
    ILabPublicContentReader labs,
    ICourseMembership membership,
    ILabPublicationSource publications) : IAccessPolicy
{
    public string Id => "lab-visibility.evaluator-key-write";
    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Subject is null ||
            request.Resource is not { Type: "lab" } resource ||
            !Guid.TryParse(request.Subject.Id, out var userId) ||
            !Guid.TryParse(resource.Id, out var labId))
        {
            return false;
        }

        var lab = await labs.GetAsync(labId, cancellationToken);
        if (lab is null || lab.State != LabVisibilityState.Draft)
        {
            return false;
        }

        var role = await membership.GetRoleAsync(lab.CourseId, userId, cancellationToken);
        if (role != CourseRole.Teacher)
        {
            return false;
        }

        var publication = await publications.GetAsync(labId, cancellationToken);
        return publication?.Status == LabPublicationStatus.Draft;
    }
}