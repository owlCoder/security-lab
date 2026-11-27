using Platform.Api.Modules.Laboratories.Domain;

namespace Laboratories.Tests;

public class LaboratoryTests
{
    private static readonly Guid CreatorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Create_AssignsCreatorDraftStatusAndServerTimeInUtc()
    {
        var clock = new FixedTimeProvider(CreatedAt.ToOffset(TimeSpan.FromHours(2)));

        var lab = new Laboratory(CourseId, "Title", CreatorId, clock);

        Assert.That(lab.Status, Is.EqualTo(LabStatus.Draft));
        Assert.That(lab.CreatedBy, Is.EqualTo(CreatorId));
        Assert.That(lab.CreatedAt, Is.EqualTo(CreatedAt));
        Assert.That(lab.CreatedAt.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Create_WithEmptyCreator_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Laboratory(CourseId, "Title", Guid.Empty));
    }

    // ---------- LAB-03: kurs ----------

    [Test]
    public void Create_AssignsCourse()
    {
        var lab = new Laboratory(CourseId, "Title", CreatorId);

        Assert.That(lab.CourseId, Is.EqualTo(CourseId));
    }

    [Test]
    public void Create_WithEmptyCourse_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Laboratory(Guid.Empty, "Title", CreatorId));

        Assert.That(exception!.ParamName, Is.EqualTo("courseId"));
    }

    [Test]
    public void Rehydrate_WithEmptyCourse_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Laboratory.Rehydrate(Guid.NewGuid(), Guid.Empty, "Title", LabStatus.Draft, CreatorId, CreatedAt));
    }

    [Test]
    public void Rehydrate_WithoutCourseColumn_LeavesCourseEmpty()
    {
        // Privremeni put za skladiste bez kolone CourseId: laboratorija nema kurs.
        var lab = Laboratory.Rehydrate(Guid.NewGuid(), "Title", LabStatus.Draft, CreatorId, CreatedAt);

        Assert.That(lab.CourseId, Is.EqualTo(Guid.Empty));
    }

    // ---------- Rekonstrukcija ----------

    [Test]
    public void Rehydrate_PreservesStoredIdentityStatusAndCreationData()
    {
        Guid id = Guid.NewGuid();

        var lab = Laboratory.Rehydrate(id, CourseId, "Title", LabStatus.Archived, CreatorId,
            CreatedAt.ToOffset(TimeSpan.FromHours(2)));

        Assert.That(lab.Id, Is.EqualTo(id));
        Assert.That(lab.CourseId, Is.EqualTo(CourseId));
        Assert.That(lab.Status, Is.EqualTo(LabStatus.Archived));
        Assert.That(lab.CreatedBy, Is.EqualTo(CreatorId));
        Assert.That(lab.CreatedAt, Is.EqualTo(CreatedAt));
        Assert.That(lab.CreatedAt.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Rehydrate_WithEmptyCreator_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Laboratory.Rehydrate(Guid.NewGuid(), CourseId, "Title", LabStatus.Draft, Guid.Empty, CreatedAt));
    }

    [TestCase(-1)]
    [TestCase(99)]
    public void Rehydrate_WithUndefinedStatus_Throws(int status)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Laboratory.Rehydrate(Guid.NewGuid(), CourseId, "Title", (LabStatus)status, CreatorId, CreatedAt));
    }

    [Test]
    public void Rehydrate_WithMissingCreationTime_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Laboratory.Rehydrate(Guid.NewGuid(), CourseId, "Title", LabStatus.Draft, CreatorId, default));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Rename_PreservesIdentityStatusAndCreationData(bool invalidTitle)
    {
        Guid id = Guid.NewGuid();
        var lab = Laboratory.Rehydrate(id, CourseId, "Original title", LabStatus.Archived, CreatorId, CreatedAt);

        if (invalidTitle)
        {
            Assert.Throws<ArgumentException>(() => lab.Rename(" "));
        }
        else
        {
            lab.Rename("Updated title");
        }

        Assert.That(lab.Id, Is.EqualTo(id));
        Assert.That(lab.CourseId, Is.EqualTo(CourseId));
        Assert.That(lab.Status, Is.EqualTo(LabStatus.Archived));
        Assert.That(lab.CreatedBy, Is.EqualTo(CreatorId));
        Assert.That(lab.CreatedAt, Is.EqualTo(CreatedAt));
        Assert.That(lab.Title, Is.EqualTo(invalidTitle ? "Original title" : "Updated title"));
    }
    [Test]
    public void Create_GeneratesDistinctNonEmptyIds()
    {
        var first = new Laboratory(CourseId, "SQL injection", CreatorId);
        var second = new Laboratory(CourseId, "SQL injection", CreatorId);

        Assert.That(first.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(second.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WithMissingTitle_Throws(string? title)
    {
        Assert.Throws<ArgumentException>(() => new Laboratory(CourseId, title!, CreatorId));
    }

    [Test]
    public void Create_WithOverlongTitle_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Laboratory(CourseId, new string('a', Laboratory.MaxTitleLength + 1), CreatorId));
    }

    [Test]
    public void Create_WithMaximumLengthTitle_Accepts()
    {
        string title = new string('a', Laboratory.MaxTitleLength);

        var lab = new Laboratory(CourseId, title, CreatorId);

        Assert.That(lab.Title, Is.EqualTo(title));
    }

    [Test]
    public void Create_WithEmptyExistingId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Laboratory.Rehydrate(Guid.Empty, CourseId, "SQL injection", LabStatus.Draft, CreatorId, CreatedAt));
    }

    [Test]
    public void Rename_KeepsSameId()
    {
        Guid id = Guid.NewGuid();
        Laboratory lab = Laboratory.Rehydrate(id, CourseId, "SQL injection", LabStatus.Draft, CreatorId, CreatedAt);

        lab.Rename("SQL injection - napredno");

        Assert.That(lab.Id, Is.EqualTo(id));
        Assert.That(lab.Title, Is.EqualTo("SQL injection - napredno"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Rename_WithMissingTitle_ThrowsAndPreservesState(string? title)
    {
        var lab = new Laboratory(CourseId, "Original title", CreatorId);
        Guid originalId = lab.Id;

        Assert.Throws<ArgumentException>(() => lab.Rename(title!));

        Assert.That(lab.Id, Is.EqualTo(originalId));
        Assert.That(lab.Title, Is.EqualTo("Original title"));
    }

    [Test]
    public void Rename_WithOverlongTitle_ThrowsAndPreservesState()
    {
        var lab = new Laboratory(CourseId, "Original title", CreatorId);
        Guid originalId = lab.Id;

        Assert.Throws<ArgumentException>(() =>
            lab.Rename(new string('a', Laboratory.MaxTitleLength + 1)));

        Assert.That(lab.Id, Is.EqualTo(originalId));
        Assert.That(lab.Title, Is.EqualTo("Original title"));
    }

    [Test]
    public void Rename_WithMaximumLengthTitle_AcceptsAndKeepsId()
    {
        var lab = new Laboratory(CourseId, "Original title", CreatorId);
        Guid originalId = lab.Id;
        string title = new string('a', Laboratory.MaxTitleLength);

        lab.Rename(title);

        Assert.That(lab.Id, Is.EqualTo(originalId));
        Assert.That(lab.Title, Is.EqualTo(title));
    }

    [Test]
    public void CreateAndRename_TrimSurroundingWhitespace()
    {
        var lab = new Laboratory(CourseId, "  Original title  ", CreatorId);
        Assert.That(lab.Title, Is.EqualTo("Original title"));

        lab.Rename("  Updated title  ");

        Assert.That(lab.Title, Is.EqualTo("Updated title"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
