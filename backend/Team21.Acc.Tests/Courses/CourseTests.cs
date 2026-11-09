using Team21.Acc.Core.Courses;

namespace Team21.Acc.Tests.Courses;

public class CourseTests
{
    [Test]
    public void NewCourseHasStableIdentityAndAllowsEnrollment()
    {
        var actor = Guid.NewGuid();
        var course = new Course(" oib-2026 ", "  OIB  ", "  Vežbe  ", actor);

        Assert.Multiple(() =>
        {
            Assert.That(course.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(course.Code, Is.EqualTo("OIB-2026"));
            Assert.That(course.Name, Is.EqualTo("OIB"));
            Assert.That(course.Description, Is.EqualTo("Vežbe"));
            Assert.That(course.CreatedBy, Is.EqualTo(actor));
            Assert.That(course.Status, Is.EqualTo(CourseStatus.Active));
            Assert.That(course.CanEnroll, Is.True);
            Assert.That(course.ArchivedAt, Is.Null);
            Assert.That(course.ArchivedBy, Is.Null);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\n")]
    public void EmptyNameIsRejected(string? name) =>
        Assert.Throws(Is.InstanceOf<ArgumentException>(), () => new Course("OIB", name!, null, Guid.NewGuid()));

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\n")]
    public void EmptyCodeIsRejected(string? code) =>
        Assert.Throws(Is.InstanceOf<ArgumentException>(), () => new Course(code!, "OIB", null, Guid.NewGuid()));

    [Test]
    public void LengthLimitsAreEnforced()
    {
        Assert.DoesNotThrow(() => new Course(new string('C', Course.MaxCodeLength), new string('n', Course.MaxNameLength),
            new string('d', Course.MaxDescriptionLength), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new Course("OIB", new string('n', Course.MaxNameLength + 1),
            null, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new Course("OIB", "OIB",
            new string('d', Course.MaxDescriptionLength + 1), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new Course(new string('C', Course.MaxCodeLength + 1),
            "OIB", null, Guid.NewGuid()));
    }

    [Test]
    public void DescriptionIsOptional() =>
        Assert.That(new Course("OIB", "OIB", null, Guid.NewGuid()).Description, Is.Empty);

    [Test]
    public void CreatorAndArchiveActorAreRequired()
    {
        Assert.Throws<ArgumentException>(() => new Course("OIB", "OIB", null, Guid.Empty));
        var course = new Course("OIB", "OIB", null, Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => course.Archive(Guid.Empty));
        Assert.That(course.CanEnroll, Is.True);
    }

    [Test]
    public void ArchiveDisallowsNewEnrollmentAndPreservesCourseData()
    {
        var course = new Course("OIB", "OIB", "Istorija", Guid.NewGuid());
        var original = (course.Id, course.Code, course.Name, course.Description, course.CreatedBy, course.CreatedAt);
        var actor = Guid.NewGuid();
        var before = DateTime.UtcNow;

        course.Archive(actor);

        Assert.Multiple(() =>
        {
            Assert.That(course.Status, Is.EqualTo(CourseStatus.Archived));
            Assert.That(course.CanEnroll, Is.False);
            Assert.That(course.IsDeleted, Is.False);
            Assert.That(course.DeletedAt, Is.Null);
            Assert.That(course.DeletedBy, Is.Null);
            Assert.That(course.ArchivedAt, Is.GreaterThanOrEqualTo(before));
            Assert.That(course.ArchivedAt!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(course.ArchivedBy, Is.EqualTo(actor));
            Assert.That(course.UpdatedBy, Is.EqualTo(actor));
            Assert.That((course.Id, course.Code, course.Name, course.Description, course.CreatedBy, course.CreatedAt),
                Is.EqualTo(original));
        });
    }

    [Test]
    public void RepeatedArchiveKeepsOriginalActorAndTime()
    {
        var course = new Course("OIB", "OIB", null, Guid.NewGuid());
        course.Archive(Guid.NewGuid());
        var original = (course.ArchivedAt, course.ArchivedBy, course.UpdatedBy);

        course.Archive(Guid.NewGuid());

        Assert.That((course.ArchivedAt, course.ArchivedBy, course.UpdatedBy), Is.EqualTo(original));
    }

    [Test]
    public void SoftDeletedCourseDoesNotAllowEnrollmentOrArchiving()
    {
        var course = new Course("OIB", "OIB", null, Guid.NewGuid()) { IsDeleted = true };
        Assert.That(course.CanEnroll, Is.False);
        Assert.Throws<InvalidOperationException>(() => course.Archive(Guid.NewGuid()));
    }
}
