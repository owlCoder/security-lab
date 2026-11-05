using Platform.Api.Modules.Laboratories.Domain;

namespace Laboratories.Tests;

public class LaboratoryTests
{
    private static readonly Guid CreatorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Create_AssignsCreatorDraftStatusAndServerTimeInUtc()
    {
        var clock = new FixedTimeProvider(CreatedAt.ToOffset(TimeSpan.FromHours(2)));

        var lab = new Laboratory("Title", CreatorId, clock);

        Assert.That(lab.Status, Is.EqualTo(LabStatus.Draft));
        Assert.That(lab.CreatedBy, Is.EqualTo(CreatorId));
        Assert.That(lab.CreatedAt, Is.EqualTo(CreatedAt));
        Assert.That(lab.CreatedAt.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Create_WithEmptyCreator_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Laboratory("Title", Guid.Empty));
    }

    [Test]
    public void Rehydrate_PreservesStoredIdentityStatusAndCreationData()
    {
        Guid id = Guid.NewGuid();

        var lab = Laboratory.Rehydrate(id, "Title", LabStatus.Archived, CreatorId,
            CreatedAt.ToOffset(TimeSpan.FromHours(2)));

        Assert.That(lab.Id, Is.EqualTo(id));
        Assert.That(lab.Status, Is.EqualTo(LabStatus.Archived));
        Assert.That(lab.CreatedBy, Is.EqualTo(CreatorId));
        Assert.That(lab.CreatedAt, Is.EqualTo(CreatedAt));
        Assert.That(lab.CreatedAt.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Rehydrate_WithEmptyCreator_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Laboratory.Rehydrate(Guid.NewGuid(), "Title", LabStatus.Draft, Guid.Empty, CreatedAt));
    }

    [TestCase(-1)]
    [TestCase(99)]
    public void Rehydrate_WithUndefinedStatus_Throws(int status)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Laboratory.Rehydrate(Guid.NewGuid(), "Title", (LabStatus)status, CreatorId, CreatedAt));
    }

    [Test]
    public void Rehydrate_WithMissingCreationTime_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Laboratory.Rehydrate(Guid.NewGuid(), "Title", LabStatus.Draft, CreatorId, default));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Rename_PreservesIdentityStatusAndCreationData(bool invalidTitle)
    {
        Guid id = Guid.NewGuid();
        var lab = Laboratory.Rehydrate(id, "Original title", LabStatus.Archived, CreatorId, CreatedAt);

        if (invalidTitle)
        {
            Assert.Throws<ArgumentException>(() => lab.Rename(" "));
        }
        else
        {
            lab.Rename("Updated title");
        }

        Assert.That(lab.Id, Is.EqualTo(id));
        Assert.That(lab.Status, Is.EqualTo(LabStatus.Archived));
        Assert.That(lab.CreatedBy, Is.EqualTo(CreatorId));
        Assert.That(lab.CreatedAt, Is.EqualTo(CreatedAt));
        Assert.That(lab.Title, Is.EqualTo(invalidTitle ? "Original title" : "Updated title"));
    }
    [Test]
    public void Create_GeneratesDistinctNonEmptyIds()
    {
        var first = new Laboratory("SQL injection", CreatorId);
        var second = new Laboratory("SQL injection", CreatorId);

        Assert.That(first.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(second.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WithMissingTitle_Throws(string? title)
    {
        Assert.Throws<ArgumentException>(() => new Laboratory(title!, CreatorId));
    }

    [Test]
    public void Create_WithOverlongTitle_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Laboratory(new string('a', Laboratory.MaxTitleLength + 1), CreatorId));
    }

    [Test]
    public void Create_WithMaximumLengthTitle_Accepts()
    {
        string title = new string('a', Laboratory.MaxTitleLength);

        var lab = new Laboratory(title, CreatorId);

        Assert.That(lab.Title, Is.EqualTo(title));
    }

    [Test]
    public void Create_WithEmptyExistingId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Laboratory.Rehydrate(Guid.Empty, "SQL injection", LabStatus.Draft, CreatorId, CreatedAt));
    }

    [Test]
    public void Rename_KeepsSameId()
    {
        Guid id = Guid.NewGuid();
        Laboratory lab = Laboratory.Rehydrate(id, "SQL injection", LabStatus.Draft, CreatorId, CreatedAt);

        lab.Rename("SQL injection - napredno");

        Assert.That(lab.Id, Is.EqualTo(id));
        Assert.That(lab.Title, Is.EqualTo("SQL injection - napredno"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Rename_WithMissingTitle_ThrowsAndPreservesState(string? title)
    {
        var lab = new Laboratory("Original title", CreatorId);
        Guid originalId = lab.Id;

        Assert.Throws<ArgumentException>(() => lab.Rename(title!));

        Assert.That(lab.Id, Is.EqualTo(originalId));
        Assert.That(lab.Title, Is.EqualTo("Original title"));
    }

    [Test]
    public void Rename_WithOverlongTitle_ThrowsAndPreservesState()
    {
        var lab = new Laboratory("Original title", CreatorId);
        Guid originalId = lab.Id;

        Assert.Throws<ArgumentException>(() =>
            lab.Rename(new string('a', Laboratory.MaxTitleLength + 1)));

        Assert.That(lab.Id, Is.EqualTo(originalId));
        Assert.That(lab.Title, Is.EqualTo("Original title"));
    }

    [Test]
    public void Rename_WithMaximumLengthTitle_AcceptsAndKeepsId()
    {
        var lab = new Laboratory("Original title", CreatorId);
        Guid originalId = lab.Id;
        string title = new string('a', Laboratory.MaxTitleLength);

        lab.Rename(title);

        Assert.That(lab.Id, Is.EqualTo(originalId));
        Assert.That(lab.Title, Is.EqualTo(title));
    }

    [Test]
    public void CreateAndRename_TrimSurroundingWhitespace()
    {
        var lab = new Laboratory("  Original title  ", CreatorId);
        Assert.That(lab.Title, Is.EqualTo("Original title"));

        lab.Rename("  Updated title  ");

        Assert.That(lab.Title, Is.EqualTo("Updated title"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
