using System.Text.RegularExpressions;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Assignments.Contracts;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using AccessMembershipReader = Platform.Access.Contracts.IMembershipReader;
using PlatformAccessDecision = Platform.Access.Contracts.IAccessDecision;

namespace Platform.Api.Modules.Assignments.Integration;

// ASN-04 / ASN-06 TIM 15: produkciono povezivanje portova autorizacije (celina 4) sa Access modulom.
//
// Napisano u okviru celine 5 uz dozvolu člana 4, jer recenzija PR-a traži da portovi u produkciji
// ne ostanu na fail-closed podrazumevanim implementacijama. Odluku NE donosi ovaj kod: on samo
// prevodi pitanje porta u AccessRequest i pita centralni IAccessDecision (ACC-05, DefaultDenyAccessDecision),
// koji zna katalog radnji, politike po ulozi na kursu i podrazumevano odbija.
//
//   IStartAttemptAuthorization      -> radnja "attempt.create"    (Student na kursu dodele)
//   IDeadlineExtensionAuthorization -> radnja "assignment.manage" (Teacher na kursu dodele)
//   ICourseEnrollmentCheck          -> IMembershipReader: aktivno članstvo sa ulogom Student
//
// Kurs se NE uzima iz parametra porta: Access ga sam razrešava iz resursa ("assignment", id)
// preko AssignmentResourceDescriptorResolver, tj. iz same dodele u skladištu.

/// <summary>
/// Zajednički prolaz za oba porta autorizacije. Svaka greška, nepoznat ili neaktivan nalog,
/// ID koji nije GUID ili kurs koji nije GUID daju <c>false</c> (fail-closed, ACC-05).
/// </summary>
public sealed partial class AssignmentAccessGate(
    PlatformAccessDecision accessDecision,
    IAccountStatusReader accounts,
    ICorrelationContext correlation,
    ILogger<AssignmentAccessGate> logger)
{
    public const string AssignmentResourceType = "assignment";

    public static readonly EventId DecisionFailedEvent = new(41504, "AssignmentAccessDecisionFailed");

    // Isto pravilo kao AuditContext.CorrelationId.
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    public async ValueTask<bool> IsAllowedAsync(
        string action,
        string? actorId,
        Guid assignmentId,
        string? courseId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseId(actorId, out var accountId)
            || assignmentId == Guid.Empty
            // Assignment.CourseId je string, a Access radi sa Guid kursom (otvorena tačka iz
            // ASN-06-contracts.md). Kurs koji nije GUID se ne može proveriti, pa se odbija.
            || !TryParseId(courseId, out _))
        {
            return false;
        }

        try
        {
            var state = await GetAccountStateAsync(accountId, cancellationToken);

            var decision = await accessDecision.DecideAsync(
                new AccessRequest(
                    new SubjectRef(accountId.ToString("D"), state),
                    action,
                    new ResourceRef(AssignmentResourceType, assignmentId.ToString("D")),
                    GetCorrelationId()),
                cancellationToken);

            return decision.Outcome == AccessOutcome.Allow;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Bez poruke izuzetka (može da sadrži connection string ili ID-jeve).
            logger.LogError(DecisionFailedEvent,
                "Assignment access decision for {Action} failed with {ExceptionType}; access denied.",
                action, exception.GetType().Name);
            return false;
        }
    }

    private async ValueTask<AccountState> GetAccountStateAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // Odbrana u dubini: AssignmentCaller već zahteva aktivan nalog, ali port može da pozove i
        // neki drugi pozivalac. Status se čita iz Identity modula, nikad od klijenta.
        var account = await accounts.FindAsync(accountId, cancellationToken);
        if (account is null)
        {
            return AccountState.Unknown;
        }

        return account.Status switch
        {
            AccountStatus.Active => AccountState.Active,
            AccountStatus.Blocked => AccountState.Blocked,
            AccountStatus.Archived => AccountState.Archived,
            _ => AccountState.Unknown,
        };
    }

    private string GetCorrelationId()
    {
        var current = correlation.CorrelationId;
        return current is not null && CorrelationIdPattern().IsMatch(current)
            ? current
            : Guid.NewGuid().ToString("N");
    }

    internal static bool TryParseId(string? value, out Guid id)
    {
        id = Guid.Empty;
        return value is not null
            && value.Length <= 64
            && Guid.TryParse(value, out id)
            && id != Guid.Empty;
    }
}

/// <summary>ASN-04: sme li student da započne pokušaj (radnja <c>attempt.create</c>).</summary>
public sealed class AccessDecisionStartAttemptAuthorization(AssignmentAccessGate gate) : IStartAttemptAuthorization
{
    public ValueTask<bool> CanStartAsync(
        string studentId,
        Guid assignmentId,
        string courseId,
        CancellationToken cancellationToken = default) =>
        gate.IsAllowedAsync(AccessActions.AttemptCreate.Id, studentId, assignmentId, courseId, cancellationToken);
}

/// <summary>ASN-06: sme li akter da menja rokove dodele (radnja <c>assignment.manage</c>, samo nastavnik kursa).</summary>
public sealed class AccessDecisionDeadlineExtensionAuthorization(AssignmentAccessGate gate) : IDeadlineExtensionAuthorization
{
    public ValueTask<bool> CanExtendAsync(
        string actorId,
        Guid assignmentId,
        string courseId,
        CancellationToken cancellationToken = default) =>
        gate.IsAllowedAsync(AccessActions.AssignmentManage.Id, actorId, assignmentId, courseId, cancellationToken);
}

/// <summary>
/// ASN-06: da li je student aktivan član kursa sa ulogom Student. Odgovara samo da/ne; podaci o
/// članstvu ne izlaze iz Access modula (ACC-17). Izvor članstva je <c>IMembershipReader</c> (tim 3).
/// </summary>
public sealed class MembershipCourseEnrollmentCheck(
    AccessMembershipReader memberships,
    ILogger<MembershipCourseEnrollmentCheck> logger) : ICourseEnrollmentCheck
{
    public async ValueTask<bool> IsActiveStudentAsync(
        string studentId,
        string courseId,
        CancellationToken cancellationToken = default)
    {
        if (!AssignmentAccessGate.TryParseId(studentId, out var accountId)
            || !AssignmentAccessGate.TryParseId(courseId, out var course))
        {
            return false;
        }

        try
        {
            var membership = await memberships.GetAsync(accountId, course, cancellationToken);

            return membership is not null
                && membership.AccountId == accountId
                && membership.CourseId == course
                && membership.Role == CourseRole.Student
                && membership.Status == MembershipStatus.Active;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(AssignmentAccessGate.DecisionFailedEvent,
                "Course enrollment check failed with {ExceptionType}; treated as not enrolled.",
                exception.GetType().Name);
            return false;
        }
    }
}
