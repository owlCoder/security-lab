using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Membership.Contracts;
using Platform.Membership.Domain;
namespace Platform.Membership.Application;

public sealed class MembershipService(
 IAccessDecision accessDecision,
 ICourseReader courses,
 IAccountStatusReader accounts,
 IMembershipStore store,
 TimeProvider clock) : IMembershipService
{
    public async ValueTask<EnrollmentResult> EnrollStudentAsync(
    SubjectRef? caller,
    Guid courseId,
    Guid studentAccountId,
    string correlationId,
    CancellationToken cancellationToken = default)
    {
        // 1. Ko sme da upisuje (ACC-02-4). Provera je ovde, u servisu, pa važi za svaki
        // put do upisa, ne samo za HTTP endpoint. Bez registrovane politike: odbijanje (ACC-05).
        var decision = await accessDecision.DecideAsync(
        new AccessRequest(
        caller,
        MembershipActions.EnrollMember,
        new ResourceRef(MembershipActions.CourseResourceType, courseId.ToString("D")),
        correlationId),
        cancellationToken);
        if (decision.Outcome != AccessOutcome.Allow)
        {
            return EnrollmentResult.Of(decision.DenialReason == AccessDenialReason.Unauthenticated
            ? EnrollmentOutcome.Unauthenticated
            : EnrollmentOutcome.Forbidden);
        }

        if (caller is null || !Guid.TryParse(caller.Id, out var actorId) || actorId == Guid.Empty)
        {
            return EnrollmentResult.Of(EnrollmentOutcome.Forbidden);
        }

        // 2. Ulaz se proverava tek posle autorizacije, da neovlašćen pozivalac ne sazna ništa.
        if (studentAccountId == Guid.Empty)
        {
            return EnrollmentResult.Of(EnrollmentOutcome.InvalidInput);
        }
        // 3. Kurs mora da postoji i da nije arhiviran (ACC-01: arhiviranje sprečava nove upise).
        var course = await courses.FindAsync(courseId, cancellationToken);
        if (course is null)
        {
            return EnrollmentResult.Of(EnrollmentOutcome.CourseNotFound);
        }
        if (course.IsArchived)
        {
            return EnrollmentResult.Of(EnrollmentOutcome.CourseArchived);
        }
        // 4. Nalog mora da postoji i da bude aktivan (IDN-03).
        var accountState = await accounts.GetStatusAsync(studentAccountId, cancellationToken);
        if (accountState is null)
        {
            return EnrollmentResult.Of(EnrollmentOutcome.AccountNotFound);
        }
        if (accountState != AccountState.Active)
        {
            return EnrollmentResult.Of(EnrollmentOutcome.AccountNotActive);
        }
        // 5. Nema duplog aktivnog upisa (ACC-02). Ova provera daje lepu poruku;
        // pravu zaštitu od dva istovremena zahteva daje jedinstveni indeks u bazi.
        if (await store.FindActiveAsync(courseId, studentAccountId, cancellationToken) is not null)
        {
            return EnrollmentResult.Of(EnrollmentOutcome.AlreadyEnrolled);
        }
        var member = CourseMember.Enroll(
        courseId,
        studentAccountId,
        MemberRole.Student,
        actorId,
        clock.GetUtcNow());
        var added = await store.AddAsync(member, cancellationToken);
        // TODO(ACC-02, AUD-01, spec. 2.1): upis je promena prava i treba da ostavi audit zapis.
        // Katalog Platform.Audit već ima odgovarajući tip: course.member.role.changed,
        // CourseId = kurs, Resource = (Account, student), oldRole = "none", newRole = "student".
        // Upisuje se kada audit modul registruje IAuditWriter (AddAudit je za sada prazan),
        // u istoj transakciji sa upisom (dogovor S11-06).
        return added == AddMemberResult.Added
        ? EnrollmentResult.Enrolled(member)
        : EnrollmentResult.Of(EnrollmentOutcome.AlreadyEnrolled);
    }
}