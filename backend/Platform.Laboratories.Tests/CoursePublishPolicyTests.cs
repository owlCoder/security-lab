using NUnit.Framework;
using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Models;

namespace Platform.Laboratories.Tests;

public class CoursePublishPolicyTests
{
    [TestCase(true, true, RevisionApprovalOutcome.SelfApprovalForbidden)]
    [TestCase(true, false, RevisionApprovalOutcome.Allowed)]
    [TestCase(false, true, RevisionApprovalOutcome.Allowed)]
    [TestCase(false, false, RevisionApprovalOutcome.Allowed)]
    public void EvaluateApproval_ReturnsExpectedOutcome(
        bool requiresIndependentApproval,
        bool isAuthor,
        RevisionApprovalOutcome expected)
    {
        var creation = CoursePublishPolicy.Create(Guid.NewGuid(), requiresIndependentApproval);
        Assert.That(creation, Is.TypeOf<CoursePublishPolicy.CreationResult.Success>());
        var policy = ((CoursePublishPolicy.CreationResult.Success)creation).Value;

        var result = policy.EvaluateApproval(isAuthor);

        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Create_WithValidCourseId_PreservesCourseAndRule(bool requiresIndependentApproval)
    {
        var courseId = Guid.NewGuid();

        var result = CoursePublishPolicy.Create(courseId, requiresIndependentApproval);

        Assert.That(result, Is.TypeOf<CoursePublishPolicy.CreationResult.Success>());
        var policy = ((CoursePublishPolicy.CreationResult.Success)result).Value;
        Assert.Multiple(() =>
        {
            Assert.That(policy.CourseId, Is.EqualTo(courseId));
            Assert.That(policy.RequiresIndependentApproval, Is.EqualTo(requiresIndependentApproval));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Create_WithEmptyCourseId_ReturnsInvalidCourseId(bool requiresIndependentApproval)
    {
        var result = CoursePublishPolicy.Create(Guid.Empty, requiresIndependentApproval);

        Assert.That(result, Is.EqualTo(new CoursePublishPolicy.CreationResult.Failure(
            CoursePublishPolicy.CreationError.InvalidCourseId)));
    }
}
