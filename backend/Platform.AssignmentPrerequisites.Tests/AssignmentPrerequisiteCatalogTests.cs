using Platform.AssignmentPrerequisites.Application;
using Platform.AssignmentPrerequisites.Domain;
using Platform.AssignmentPrerequisites.Infrastructure;
using Platform.AssignmentPrerequisites.Tests.Support;
using static Platform.AssignmentPrerequisites.Tests.Support.TestData;

namespace Platform.AssignmentPrerequisites.Tests;

[TestFixture]
public class AssignmentPrerequisiteCatalogTests
{
    private InMemoryAssignmentPrerequisiteStore _store = null!;
    private AssignmentPrerequisiteCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryAssignmentPrerequisiteStore();
        var assignments = new FakeAssignmentRepository(
            AssignmentIn(AssignmentA, Course),
            AssignmentIn(AssignmentB, Course),
            AssignmentIn(AssignmentC, Course),
            AssignmentIn(AssignmentOtherCourse, OtherCourse));
        _catalog = new AssignmentPrerequisiteCatalog(_store, assignments);
    }

    [Test]
    public async Task ValidPrerequisite_IsAdded_AndCanBeRead()
    {
        AddPrerequisiteOutcome outcome = await _catalog.AddAsync(Requires(AssignmentB, AssignmentA));
        IReadOnlyList<AssignmentPrerequisite> forB = await _catalog.GetForAssignmentAsync(AssignmentB);

        Assert.That(outcome, Is.EqualTo(AddPrerequisiteOutcome.Added));
        Assert.That(forB.Select(p => p.RequiredAssignmentId), Is.EqualTo(new[] { AssignmentA }));
    }

    [Test]
    public async Task Duplicate_IsRejected_AndStoredOnce()
    {
        await _catalog.AddAsync(Requires(AssignmentB, AssignmentA));

        AddPrerequisiteOutcome outcome = await _catalog.AddAsync(Requires(AssignmentB, AssignmentA));

        Assert.That(outcome, Is.EqualTo(AddPrerequisiteOutcome.Duplicate));
        Assert.That(await _store.GetForCourseAsync(Course), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Cycle_IsRejected_AndNothingIsWritten()
    {
        await _catalog.AddAsync(Requires(AssignmentB, AssignmentA));
        await _catalog.AddAsync(Requires(AssignmentC, AssignmentB));

        AddPrerequisiteOutcome outcome = await _catalog.AddAsync(Requires(AssignmentA, AssignmentC));

        Assert.That(outcome, Is.EqualTo(AddPrerequisiteOutcome.WouldCreateCycle));
        Assert.That(await _store.GetForAssignmentAsync(AssignmentA), Is.Empty);
    }

    [Test]
    public async Task RequiredAssignmentFromOtherCourse_IsRejected()
    {
        AddPrerequisiteOutcome outcome = await _catalog.AddAsync(Requires(AssignmentB, AssignmentOtherCourse));

        Assert.That(outcome, Is.EqualTo(AddPrerequisiteOutcome.AssignmentNotInCourse));
        Assert.That(await _store.GetForCourseAsync(Course), Is.Empty);
    }

    [Test]
    public async Task CourseIdThatDoesNotMatchAssignments_IsRejected()
    {
        AddPrerequisiteOutcome outcome = await _catalog.AddAsync(
            Requires(AssignmentB, AssignmentA, course: OtherCourse));

        Assert.That(outcome, Is.EqualTo(AddPrerequisiteOutcome.AssignmentNotInCourse));
        Assert.That(await _store.GetForCourseAsync(OtherCourse), Is.Empty);
    }

    [Test]
    public async Task UnknownAssignment_IsRejected()
    {
        AddPrerequisiteOutcome outcome = await _catalog.AddAsync(Requires(Guid.NewGuid(), AssignmentA));

        Assert.That(outcome, Is.EqualTo(AddPrerequisiteOutcome.AssignmentNotInCourse));
    }

    [Test]
    public async Task Remove_DeletesOnlyThatRelation()
    {
        await _catalog.AddAsync(Requires(AssignmentC, AssignmentA));
        await _catalog.AddAsync(Requires(AssignmentC, AssignmentB));

        bool removed = await _catalog.RemoveAsync(AssignmentC, AssignmentA);
        bool removedAgain = await _catalog.RemoveAsync(AssignmentC, AssignmentA);
        IReadOnlyList<AssignmentPrerequisite> left = await _catalog.GetForAssignmentAsync(AssignmentC);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(removedAgain, Is.False);
            Assert.That(left.Select(p => p.RequiredAssignmentId), Is.EqualTo(new[] { AssignmentB }));
        });
    }

    [Test]
    public void EmptyAssignmentId_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => _catalog.GetForAssignmentAsync(Guid.Empty));
        Assert.Throws<ArgumentException>(() => _catalog.RemoveAsync(Guid.Empty, AssignmentA));
    }
}
