using Platform.AssignmentPrerequisites.Domain;
using static Platform.AssignmentPrerequisites.Tests.Support.TestData;

namespace Platform.AssignmentPrerequisites.Tests;

[TestFixture]
public class PrerequisiteGraphTests
{
    [Test]
    public void SameRelation_IsDuplicate_EvenWithDifferentRequirement()
    {
        AssignmentPrerequisite[] existing = [Requires(AssignmentB, AssignmentA)];

        bool duplicate = PrerequisiteGraph.IsDuplicate(
            existing, Requires(AssignmentB, AssignmentA, CompletionRequirement.PassedOrPartial));

        Assert.That(duplicate, Is.True);
    }

    [Test]
    public void ReverseDirection_IsNotDuplicate()
    {
        AssignmentPrerequisite[] existing = [Requires(AssignmentB, AssignmentA)];

        Assert.That(PrerequisiteGraph.IsDuplicate(existing, Requires(AssignmentA, AssignmentB)), Is.False);
    }

    [Test]
    public void TwoAssignmentCycle_IsDetected()
    {
        AssignmentPrerequisite[] existing = [Requires(AssignmentB, AssignmentA)];

        Assert.That(PrerequisiteGraph.WouldCreateCycle(existing, Requires(AssignmentA, AssignmentB)), Is.True);
    }

    [Test]
    public void ThreeAssignmentCycle_IsDetected()
    {
        AssignmentPrerequisite[] existing =
        [
            Requires(AssignmentC, AssignmentB),
            Requires(AssignmentB, AssignmentA)
        ];

        Assert.That(PrerequisiteGraph.WouldCreateCycle(existing, Requires(AssignmentA, AssignmentC)), Is.True);
    }

    [Test]
    public void Chain_WithoutCycle_IsAllowed()
    {
        AssignmentPrerequisite[] existing = [Requires(AssignmentB, AssignmentA)];

        Assert.That(PrerequisiteGraph.WouldCreateCycle(existing, Requires(AssignmentC, AssignmentB)), Is.False);
    }

    [Test]
    public void SharedPrerequisite_IsNotACycle()
    {
        AssignmentPrerequisite[] existing =
        [
            Requires(AssignmentB, AssignmentA),
            Requires(AssignmentC, AssignmentA)
        ];

        Assert.That(PrerequisiteGraph.WouldCreateCycle(existing, Requires(AssignmentC, AssignmentB)), Is.False);
    }

    [Test]
    public void RelationsOfOtherCourse_AreIgnored()
    {
        AssignmentPrerequisite[] existing = [Requires(AssignmentB, AssignmentA, course: OtherCourse)];

        Assert.That(PrerequisiteGraph.WouldCreateCycle(existing, Requires(AssignmentA, AssignmentB)), Is.False);
    }
}
