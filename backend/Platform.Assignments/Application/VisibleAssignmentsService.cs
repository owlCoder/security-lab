// ASN-05 TIM 13
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Enums;
using Platform.Assignments.Domain.Models;
using Platform.Assignments.Domain.ValueObjects;

namespace Platform.Assignments.Application;

// The list of assignments a student may see. Each assignment goes through AssignmentGroupPolicy;
// a hidden assignment is simply left out, so the list reveals nothing about it.
public sealed class VisibleAssignmentsService
{
    // Reason code for a failed enrollment or membership check (fail closed, as in StartAttemptService).
    public const string CheckFailedCode = "CheckFailed";

    private readonly IAssignmentRepository _assignmentRepository;
    private readonly ICourseEnrollmentCheck _enrollment;
    private readonly IGroupMembership _groupMembership;
    private readonly IAssignmentVisibilityLog _visibilityLog;

    public VisibleAssignmentsService(
        IAssignmentRepository assignmentRepository,
        ICourseEnrollmentCheck enrollment,
        IGroupMembership groupMembership,
        IAssignmentVisibilityLog visibilityLog)
    {
        _assignmentRepository = assignmentRepository;
        _enrollment = enrollment;
        _groupMembership = groupMembership;
        _visibilityLog = visibilityLog;
    }

    // courseIds comes from the caller and is not trusted: every assignment is checked
    // against enrollment in its own stored CourseId.
    public async ValueTask<IReadOnlyList<Assignment>> GetVisibleForStudentAsync(
        string studentId,
        IReadOnlyCollection<string> courseIds,
        CancellationToken cancellationToken = default)
    {
        if (!PrincipalId.IsValid(studentId) || courseIds is null || courseIds.Count == 0)
        {
            return Array.Empty<Assignment>();
        }

        var assignments = await _assignmentRepository.GetForCoursesAsync(courseIds, cancellationToken);

        // Enrollment is asked once per course within one call.
        var enrollmentByCourse = new Dictionary<string, bool>(StringComparer.Ordinal);
        var visible = new List<Assignment>();

        foreach (var assignment in assignments)
        {
            var decision = await DecideAsync(assignment, studentId, enrollmentByCourse, cancellationToken);

            if (decision.Allowed)
            {
                visible.Add(assignment);
            }
            else
            {
                await _visibilityLog.RecordHiddenAsync(studentId, assignment.Id, decision.Code, cancellationToken);
            }
        }

        return visible;
    }

    private async ValueTask<AssignmentGroupDecision> DecideAsync(
        Assignment assignment,
        string studentId,
        Dictionary<string, bool> enrollmentByCourse,
        CancellationToken cancellationToken)
    {
        try
        {
            var isEnrolled = await IsEnrolledAsync(
                studentId, assignment.CourseId, enrollmentByCourse, cancellationToken);

            // The group always comes from the stored assignment, never from the request.
            var isGroupMember = false;
            if (isEnrolled && assignment.Scope == AssignmentScope.Group)
            {
                isGroupMember = await _groupMembership.IsGroupMemberAsync(
                    studentId,
                    assignment.CourseId,
                    assignment.GroupId!,
                    cancellationToken);
            }

            return AssignmentGroupPolicy.DecideVisibility(
                assignment.Scope,
                isEnrolled,
                isGroupMember);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Fail closed: an error in a check hides only this assignment.
            return new AssignmentGroupDecision(false, CheckFailedCode);
        }
    }

    private async ValueTask<bool> IsEnrolledAsync(
        string studentId,
        string courseId,
        Dictionary<string, bool> enrollmentByCourse,
        CancellationToken cancellationToken)
    {
        if (!enrollmentByCourse.TryGetValue(courseId, out var isEnrolled))
        {
            isEnrolled = await _enrollment.IsActiveStudentAsync(studentId, courseId, cancellationToken);
            enrollmentByCourse[courseId] = isEnrolled;
        }

        return isEnrolled;
    }
}
