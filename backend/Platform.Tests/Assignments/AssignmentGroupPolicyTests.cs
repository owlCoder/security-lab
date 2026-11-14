using Platform.Assignments.Application;
using Platform.Assignments.Domain.Enums;

namespace Platform.Tests.Assignments;

[TestFixture]
public sealed class AssignmentGroupPolicyTests
{
    [Test]
    public void DecideVisibility_CourseAssignmentAndEnrolledStudent_Allows()
    {
        var decision = AssignmentGroupPolicy.DecideVisibility(
            AssignmentScope.Course,
            isEnrolled: true,
            isGroupMember: false);

        Assert.That(decision, Is.EqualTo(
            new AssignmentGroupDecision(true, AssignmentGroupCodes.CourseStudent)));
    }

    [Test]
    public void DecideVisibility_GroupAssignmentAndGroupMember_Allows()
    {
        var decision = AssignmentGroupPolicy.DecideVisibility(
            AssignmentScope.Group,
            isEnrolled: true,
            isGroupMember: true);

        Assert.That(decision, Is.EqualTo(
            new AssignmentGroupDecision(true, AssignmentGroupCodes.GroupMember)));
    }

    [Test]
    public void DecideVisibility_GroupAssignmentAndNonMember_Denies()
    {
        var decision = AssignmentGroupPolicy.DecideVisibility(
            AssignmentScope.Group,
            isEnrolled: true,
            isGroupMember: false);

        Assert.That(decision, Is.EqualTo(
            new AssignmentGroupDecision(false, AssignmentGroupCodes.NotGroupMember)));
    }

    [TestCase(AssignmentScope.Course)]
    [TestCase(AssignmentScope.Group)]
    public void DecideVisibility_StudentNotEnrolled_Denies(AssignmentScope scope)
    {
        var decision = AssignmentGroupPolicy.DecideVisibility(
            scope,
            isEnrolled: false,
            isGroupMember: true);

        Assert.That(decision, Is.EqualTo(
            new AssignmentGroupDecision(false, AssignmentGroupCodes.NotEnrolled)));
    }

    [TestCase(AssignmentScope.Unspecified)]
    [TestCase((AssignmentScope)99)]
    public void DecideVisibility_UnsupportedScope_Denies(AssignmentScope scope)
    {
        var decision = AssignmentGroupPolicy.DecideVisibility(
            scope,
            isEnrolled: true,
            isGroupMember: true);

        Assert.That(decision, Is.EqualTo(
            new AssignmentGroupDecision(false, AssignmentGroupCodes.UnsupportedScope)));
    }
}