using Platform.Laboratories.Domain.Enums;

namespace Platform.Laboratories.Domain.Models;

public class CoursePublishPolicy
{
    public bool RequiresIndependentApproval { get; }
    public Guid CourseId { get; }

    private CoursePublishPolicy(Guid courseId, bool requiresIndependentApproval)
    {
        CourseId = courseId;
        RequiresIndependentApproval = requiresIndependentApproval;
    }

    public static CreationResult Create(Guid courseId, bool requiresIndependentApproval)
    {
        if (courseId == Guid.Empty)
        {
            return new CreationResult.Failure(CreationError.InvalidCourseId);
        }

        return new CreationResult.Success(
            new CoursePublishPolicy(courseId, requiresIndependentApproval));
    }

    public RevisionApprovalOutcome EvaluateApproval(bool isAuthor)
    {
        if (RequiresIndependentApproval && isAuthor)
        {
            return RevisionApprovalOutcome.SelfApprovalForbidden;
        }

        return RevisionApprovalOutcome.Allowed;
    }

    public enum CreationError
    {
        InvalidCourseId = 1
    }

    public abstract record CreationResult
    {
        private CreationResult() { }

        public sealed record Success(CoursePublishPolicy Value) : CreationResult;
        public sealed record Failure(CreationError Error) : CreationResult;
    }
}
