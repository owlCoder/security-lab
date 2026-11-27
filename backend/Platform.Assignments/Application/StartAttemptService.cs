using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;
using Platform.Assignments.Domain.Enums;

namespace Platform.Assignments.Application;

public sealed class StartAttemptService
{
    private readonly IAssignmentRepository _assignmentRepository;
    private readonly IAttemptStore _attemptStore;

    private readonly IStartAttemptAuthorization _authorization;
    private readonly IGroupMembership _groupMembership;

    public StartAttemptService(
        IAssignmentRepository assignmentRepository,
        IAttemptStore attemptStore,
        IStartAttemptAuthorization authorization,
        IGroupMembership groupMembership)
    {
        _assignmentRepository = assignmentRepository;
        _attemptStore = attemptStore;
        _authorization = authorization;
        _groupMembership = groupMembership;
    }

    // ASN-05
    // Runs after IStartAttemptAuthorization has confirmed an active student of the course,
    // so enrollment is already established here. A group assignment additionally requires
    // membership in exactly the group stored on the assignment (never a value from the request).
    // Fail closed: an error in the membership check denies access.
    private async ValueTask<bool> IsInAssignmentGroupAsync(
        Assignment assignment,
        string studentId,
        CancellationToken cancellationToken)
    {
        var isGroupMember = false;

        if (assignment.Scope == AssignmentScope.Group)
        {
            try
            {
                isGroupMember = await _groupMembership.IsGroupMemberAsync(
                    studentId,
                    assignment.CourseId,
                    assignment.GroupId!,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return false;
            }
        }

        return AssignmentGroupPolicy.DecideVisibility(
            assignment.Scope,
            isEnrolled: true,
            isGroupMember).Allowed;
    }

    private async ValueTask<Assignment?> LoadAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        return await _assignmentRepository.GetByIdAsync(
            assignmentId,
            cancellationToken);
    }

    private static bool IsAssignmentAvailable(
    Assignment assignment,
    DateTimeOffset now)
    {
        return assignment.GetAvailability(now)
            == AssignmentAvailability.Available;
    }

    
    public async ValueTask<Guid> StartAttemptAsync(
        Guid assignmentId,
        string studentId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await LoadAssignmentAsync(
            assignmentId, cancellationToken);

        if (assignment == null)
        {
            throw new KeyNotFoundException("Assignment not found.");
        }

        if (string.IsNullOrWhiteSpace(studentId))
        {
            throw new UnauthorizedAccessException("Access denied.");
        }

        if (!await _authorization.CanStartAsync(
            studentId,
            assignment.Id,
            assignment.CourseId,
            cancellationToken))
        {
            throw new UnauthorizedAccessException("Access denied.");
        }

        // ASN-05: a student of another group gets the same denial as any unauthorized caller,
        // before availability or the attempt limit are revealed.
        if (!await IsInAssignmentGroupAsync(
            assignment,
            studentId,
            cancellationToken))
        {
            throw new UnauthorizedAccessException("Access denied.");
        }

        if (!IsAssignmentAvailable(assignment, DateTimeOffset.UtcNow))
        {
            throw new InvalidOperationException("Assignment is not available.");
        }

        var attemptId = await _attemptStore.TryStartAsync(
            assignment.Id,
            studentId,
            assignment.PublishedLabRevisionId,
            assignment.MaxAttempts,
            requestId,
            cancellationToken);

        if (attemptId == null)
        {
            throw new InvalidOperationException("Maximum number of attempts reached.");
        }

        return attemptId.Value;
    }

}