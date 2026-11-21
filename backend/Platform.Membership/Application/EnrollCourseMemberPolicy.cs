using Microsoft.Extensions.Configuration;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Membership.Domain;
using Platform.Access.Domain.Enums;

namespace Platform.Membership.Application;

public sealed class EnrollCourseMemberPolicy(
    IConfiguration configuration,
    IMembershipStore store) : IAccessPolicy
{
    public string Id => "course.member.enroll.teacher-or-admin";
    public string Version => "v1";



public async ValueTask<bool> EvaluateAsync(
    AccessRequest request,
    CancellationToken cancellationToken = default)
{
    if (request.Action != MembershipActions.EnrollMember ||
        request.Subject is null ||
    request.Subject.State != AccountState.Active ||
        !Guid.TryParse(request.Subject.Id, out var callerId) ||
        callerId == Guid.Empty ||
        request.Resource is null ||
        request.Resource.Type != MembershipActions.CourseResourceType ||
        !Guid.TryParse(request.Resource.Id, out var courseId) ||
        courseId == Guid.Empty)
    {
        return false;
    }

    // Administratori se prepoznaju isto kao u Identity modulu.
    var admins = configuration
        .GetSection("Identity:AdminAccountIds")
        .Get<string[]>() ?? [];

    if (admins.Contains(request.Subject.Id, StringComparer.Ordinal))
    {
        return true;
    }

    // Ostali moraju biti aktivni nastavnici konkretnog kursa.
    var membership = await store.FindActiveAsync(
        courseId,
        callerId,
        cancellationToken);

    return membership is not null &&
           membership.CourseId == courseId &&
           membership.AccountId == callerId &&
           membership.Role == MemberRole.Teacher &&
           membership.Status == MemberStatus.Active;
}
}