using System.Collections.Frozen;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using AccessCourseRole = Platform.Access.Domain.Enums.CourseRole;
using AccessMembershipStatus = Platform.Access.Domain.Enums.MembershipStatus;

namespace Platform.Membership.Infrastructure.AccessControl;

/// <summary>
/// ACC-02-5: nasa implementacija IMembershipReader (ugovor Tima 3, ACC-05).
/// Provera pristupa (npr. lab.read) pita ovde da li je korisnik upisan u kurs.
/// Cita se iz baze pri svakom pozivu, bez kesa, pa ispis vazi vec na sledecem zahtevu.
/// Ne drzi DbContext: IMembershipStore (EfMembershipStore) za svaki poziv otvara
/// kratak kontekst preko IDbContextFactory, pa je bezbedan kao singleton.
/// </summary>
internal sealed class StoreMembershipReader(IMembershipStore store) : IMembershipReader
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

        return new CourseMembership(
            member.AccountId,
            member.CourseId,
            ToAccessRole(member.Role),
            AccessMembershipStatus.Active,
            // ACC-07 (Sprint 2): ovde idu delegirane radnje kojima rok nije istekao.
            FrozenSet<string>.Empty);
    }

    private static AccessCourseRole ToAccessRole(MemberRole role) => role switch
    {
        MemberRole.Student => AccessCourseRole.Student,
        MemberRole.Teacher => AccessCourseRole.Teacher,
        MemberRole.Assistant => AccessCourseRole.Assistant,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown course role.")
    };
}