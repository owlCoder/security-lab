using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Membership.Contracts;
using Platform.Membership.Domain;
using Platform.Audit.Contracts;
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

        var added = await store.AddAsync(
            member,
            // Akter i korelacioni ID dolaze sa servera (AUD-04), nikad iz tela zahteva.
            new AuditContext(AuditActor.ForUser(actorId), correlationId),
            cancellationToken);

        return added switch
        {
            AddMemberResult.Added => EnrollmentResult.Enrolled(member),
            AddMemberResult.AlreadyActive => EnrollmentResult.Of(EnrollmentOutcome.AlreadyEnrolled),
            _ => EnrollmentResult.Of(EnrollmentOutcome.Unavailable)
        };
    }
}