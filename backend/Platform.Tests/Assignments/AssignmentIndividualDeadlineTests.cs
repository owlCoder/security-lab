using Platform.Assignments.Domain.Enums;
using Platform.Assignments.Domain.Models;

namespace Platform.Tests.Assignments;

// ASN-06 TIM 15
[TestFixture]
public sealed class AssignmentIndividualDeadlineTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new(2026, 11, 9, 23, 59, 59, TimeSpan.Zero);

    [Test]
    public void GetAvailability_AfterDueDate_WithLaterIndividualDeadline_ReturnsAvailable()
    {
        var assignment = CreateAssignment();

        var availability = assignment.GetAvailability(
            Due.AddDays(1), Due.AddDays(3));

        Assert.That(availability, Is.EqualTo(AssignmentAvailability.Available));
    }

    [Test]
    public void GetAvailability_AfterIndividualDeadline_ReturnsExpired()
    {
        var assignment = CreateAssignment();

        var availability = assignment.GetAvailability(
            Due.AddDays(4), Due.AddDays(3));

        Assert.That(availability, Is.EqualTo(AssignmentAvailability.Expired));
    }

    [Test]
    public void GetAvailability_WithEarlierIndividualDeadline_DoesNotShortenDeadline()
    {
        var assignment = CreateAssignment();

        var availability = assignment.GetAvailability(
            Due.AddHours(-1), Due.AddDays(-5));

        Assert.That(availability, Is.EqualTo(AssignmentAvailability.Available));
        Assert.That(
            assignment.GetEffectiveDueAtUtc(Due.AddDays(-5)),
            Is.EqualTo(Due));
    }

    [Test]
    public void GetAvailability_WhenArchived_IgnoresIndividualDeadline()
    {
        var assignment = CreateAssignment();
        assignment.Archive();

        var availability = assignment.GetAvailability(
            Start, Due.AddDays(3));

        Assert.That(availability, Is.EqualTo(AssignmentAvailability.Archived));
    }

    [Test]
    public void GetEffectiveDueAtUtc_WithoutIndividualDeadline_ReturnsAssignmentDeadline()
    {
        var assignment = CreateAssignment();

        Assert.That(assignment.GetEffectiveDueAtUtc(null), Is.EqualTo(Due));
    }

    private static Assignment CreateAssignment() => new(
        Guid.NewGuid(),
        "course-oib",
        "revision-1",
        Start,
        Due,
        maxAttempts: 2,
        scope: AssignmentScope.Course);
}
