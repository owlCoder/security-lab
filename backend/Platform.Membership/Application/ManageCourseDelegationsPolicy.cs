
using Microsoft.Extensions.Configuration;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Membership.Domain;

namespace Platform.Membership.Application;

/// <summary>
/// ACC-07-3: ko sme da daje, opoziva i pregleda delegacije u kursu: aktivni nastavnik
/// TOG kursa ili administrator. Asistent ne može sam sebi da da prava, a student ništa.
/// Isto pravilo kao EnrollCourseMemberPolicy (ACC-02-4).
/// </summary>
public sealed class ManageCourseDelegationsPolicy(
    IConfiguration configuration,
    IMembershipStore store) : IAccessPolicy
{
    public string Id => "course.delegation.manage.teacher-or-admin";
    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Action is not (DelegationActions.Grant or DelegationActions.Revoke or DelegationActions.Read)
            || request.Subject is null
            || request.Subject.State != AccountState.Active
            || !Guid.TryParse(request.Subject.Id, out var callerId)
            || callerId == Guid.Empty
            || request.Resource is null
            || request.Resource.Type != DelegationActions.CourseResourceType
            || !Guid.TryParse(request.Resource.Id, out var courseId)
            || courseId == Guid.Empty)
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

        var membership = await store.FindActiveAsync(
            courseId,
            callerId,
            cancellationToken);

        return membership is not null
            && membership.CourseId == courseId
            && membership.AccountId == callerId
            && membership.Role == MemberRole.Teacher
            && membership.Status == MemberStatus.Active;
    }
}
