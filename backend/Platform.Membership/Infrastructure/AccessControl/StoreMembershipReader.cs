using System.Collections.Frozen;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using AccessCourseRole = Platform.Access.Domain.Enums.CourseRole;
using AccessMembershipStatus = Platform.Access.Domain.Enums.MembershipStatus;

namespace Platform.Membership.Infrastructure.AccessControl;

/// <summary>
/// ACC-02-5 / ACC-07-7: nasa implementacija IMembershipReader (ugovor Tima 3, ACC-05).
/// Provera pristupa (npr. lab.read) pita ovde da li je korisnik upisan u kurs i koje
/// delegirane radnje ima. Cita se iz baze pri svakom pozivu, bez kesa, po serverskom vremenu:
/// ispis, opoziv i istek delegacije vaze vec na sledecem zahtevu.
/// Ne drzi DbContext (skladista otvaraju kratak kontekst), pa je bezbedan kao singleton.
/// </summary>
internal sealed class StoreMembershipReader(
    IMembershipStore store,
    IDelegationStore delegations,
    TimeProvider clock) : IMembershipReader
{
    public async ValueTask<CourseMembership?> GetAsync(
        Guid accountId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var member = await store.FindActiveAsync(courseId, accountId, cancellationToken);
        if (member is null)
        {
            return null; // nije upisan ili je ispisan -> Tim 3 odbija
        }

        // ACC-07: Access daje delegirane radnje samo asistentu, pa se samo za njega i citaju.
        // Vreme je serversko (TimeProvider), a ne vreme koje salje klijent.
        IReadOnlySet<string> delegated = member.Role == MemberRole.Assistant
            ? await delegations.ActiveActionsAsync(courseId, accountId, clock.GetUtcNow(), cancellationToken)
            : FrozenSet<string>.Empty;

        return new CourseMembership(
            member.AccountId,
            member.CourseId,
            ToAccessRole(member.Role),
            AccessMembershipStatus.Active,
            delegated);
    }

    private static AccessCourseRole ToAccessRole(MemberRole role) => role switch
    {
        MemberRole.Student => AccessCourseRole.Student,
        MemberRole.Teacher => AccessCourseRole.Teacher,
        MemberRole.Assistant => AccessCourseRole.Assistant,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown course role.")
    };
}
