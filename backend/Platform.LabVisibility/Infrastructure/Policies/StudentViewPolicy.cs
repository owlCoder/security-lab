using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using LabVisibilityCourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.LabVisibility.Infrastructure.Policies;

public sealed class StudentViewPolicy(ILabPublicContentReader labs, ICourseMembership membership) : IAccessPolicy
{
    public string Id => LabVisibilityActions.StudentView;
    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        var subject = request.Subject;

        if (subject is null || subject.State != AccountState.Active || !Guid.TryParse(subject.Id, out var userId) || userId == Guid.Empty)
        {
            return false;
        }

        var resource = request.Resource;

        if (resource is null || !Guid.TryParse(resource.Id, out var labId) || labId == Guid.Empty)
        {
            return false;
        }

        var lab = await labs.GetAsync(labId, cancellationToken);

        if (lab is null || lab.State != LabVisibilityState.Published)
        {
            return false;
        }

        var role = await membership.GetRoleAsync(lab.CourseId, userId, cancellationToken);

        return role == LabVisibilityCourseRole.Student || role == LabVisibilityCourseRole.Teacher;
    }
}