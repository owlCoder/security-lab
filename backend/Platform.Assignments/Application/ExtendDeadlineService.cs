using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Enums;
using Platform.Assignments.Domain.Models;
using Platform.Assignments.Domain.ValueObjects;

namespace Platform.Assignments.Application;

// ASN-06 TIM 15
/// <summary>
/// Use case "extend the deadline for one student". Only an authorized instructor of the assignment's
/// course may do it. The caller must pass the actor ID of the logged-in user as resolved on the server,
/// never an ID taken from a request body. Every outcome is recorded in the audit log.
/// </summary>
public sealed class ExtendDeadlineService
{
    private const string AccessDenied = "Access denied.";

    private readonly IAssignmentRepository _assignmentRepository;
    private readonly IDeadlineExtensionAuthorization _authorization;
    private readonly ICourseEnrollmentCheck _enrollment;
    private readonly IDeadlineExtensionStore _store;
    private readonly IDeadlineExtensionAuditWriter _audit;
    private readonly TimeProvider _timeProvider;

    public ExtendDeadlineService(
        IAssignmentRepository assignmentRepository,
        IDeadlineExtensionAuthorization authorization,
        ICourseEnrollmentCheck enrollment,
        IDeadlineExtensionStore store,
        IDeadlineExtensionAuditWriter audit,
        TimeProvider timeProvider)
    {
        _assignmentRepository = assignmentRepository;
        _authorization = authorization;
        _enrollment = enrollment;
        _store = store;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    /// <exception cref="UnauthorizedAccessException">
    /// The caller may not extend this assignment. The same error is used when the assignment does not
    /// exist, so callers cannot probe for assignment IDs.
    /// </exception>
    /// <exception cref="ArgumentException">The student ID is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The new deadline is not later than the student's current deadline, or is not in the future.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The assignment is archived, the student is not an active member of the course,
    /// or the deadline was changed concurrently.
    /// </exception>
    public async ValueTask<StudentDeadlineOverride> ExtendDeadlineAsync(
        string actorId,
        Guid assignmentId,
        string studentId,
        DateTimeOffset newDueAtUtc,
        CancellationToken cancellationToken = default)
    {
        // 1. Authorize first, before any other data is read or changed.
        if (!PrincipalId.IsValid(actorId))
        {
            throw await DenyAsync(assignmentId, null, studentId);
        }

        var assignment = await _assignmentRepository.GetByIdAsync(
            assignmentId, cancellationToken);

        if (assignment is null)
        {
            throw await DenyAsync(assignmentId, null, studentId);
        }

        if (!await _authorization.CanExtendAsync(
            actorId, assignment.Id, assignment.CourseId, cancellationToken))
        {
            throw await DenyAsync(assignment.Id, assignment.CourseId, studentId);
        }

        // 2. Validate. The caller is authorized from here on, so a failure is a "Failure" outcome.
        if (!PrincipalId.IsValid(studentId))
        {
            await FailAsync(
                assignment, null, null, null, DeadlineExtensionReasonCodes.InvalidStudent);
            throw new ArgumentException("Student ID is invalid.", nameof(studentId));
        }

        if (assignment.Status == AssignmentStatus.Archived)
        {
            await FailAsync(
                assignment, studentId, null, null, DeadlineExtensionReasonCodes.Archived);
            throw new InvalidOperationException("Assignment is archived.");
        }

        if (!await _enrollment.IsActiveStudentAsync(
            studentId, assignment.CourseId, cancellationToken))
        {
            await FailAsync(
                assignment, studentId, null, null, DeadlineExtensionReasonCodes.NotEnrolled);
            throw new InvalidOperationException(
                "Student is not an active member of the course.");
        }

        var now = _timeProvider.GetUtcNow();
        var newDue = newDueAtUtc.ToUniversalTime();

        var existing = await _store.GetAsync(
            assignment.Id, studentId, cancellationToken);
        var currentDue = assignment.GetEffectiveDueAtUtc(existing?.DueAtUtc);

        if (newDue <= currentDue || newDue <= now)
        {
            await FailAsync(
                assignment, studentId, currentDue, newDue,
                DeadlineExtensionReasonCodes.InvalidDeadline);
            throw new ArgumentOutOfRangeException(
                nameof(newDueAtUtc),
                "The new deadline must be later than the current deadline and in the future.");
        }

        // 3. Change and audit record are stored as one unit (fail-closed).
        var deadlineOverride = new StudentDeadlineOverride(
            assignment.Id, studentId, newDue, currentDue, actorId, now);

        var successEntry = new DeadlineExtensionAuditEntry(
            DeadlineExtensionAuditOutcome.Success,
            assignment.Id,
            assignment.CourseId,
            studentId,
            currentDue,
            newDue,
            ReasonCode: null);

        try
        {
            await _store.SaveAsync(
                deadlineOverride, successEntry, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await FailAsync(
                assignment, studentId, currentDue, newDue,
                DeadlineExtensionReasonCodes.SaveFailed);
            throw;
        }

        return deadlineOverride;
    }

    private async ValueTask<UnauthorizedAccessException> DenyAsync(
        Guid assignmentId,
        string? courseId,
        string? studentId)
    {
        await TryAuditAsync(new DeadlineExtensionAuditEntry(
            DeadlineExtensionAuditOutcome.Denied,
            assignmentId,
            courseId,
            PrincipalId.IsValid(studentId) ? studentId : null,
            PreviousDueAtUtc: null,
            NewDueAtUtc: null,
            DeadlineExtensionReasonCodes.Forbidden));

        return new UnauthorizedAccessException(AccessDenied);
    }

    private ValueTask FailAsync(
        Assignment assignment,
        string? studentId,
        DateTimeOffset? previousDueAtUtc,
        DateTimeOffset? newDueAtUtc,
        string reasonCode)
        => TryAuditAsync(new DeadlineExtensionAuditEntry(
            DeadlineExtensionAuditOutcome.Failure,
            assignment.Id,
            assignment.CourseId,
            studentId,
            previousDueAtUtc,
            newDueAtUtc,
            reasonCode));

    // A denial or a failure stays as it is even when its audit record cannot be written;
    // an audit failure never grants access. The host adapter that implements
    // IDeadlineExtensionAuditWriter raises the technical alarm (no logger in this module).
    // CancellationToken.None: a cancelled HTTP request must not lose the record.
    private async ValueTask TryAuditAsync(DeadlineExtensionAuditEntry entry)
    {
        try
        {
            await _audit.TryWriteAsync(entry, CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }
}
