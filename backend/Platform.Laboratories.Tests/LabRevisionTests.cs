using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Models;

namespace Platform.Laboratories.Tests;

public class LabRevisionTests
{
    private const string Hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [TestCase("id")]
    [TestCase("course")]
    [TestCase("author")]
    [TestCase("hash")]
    public void Create_InvalidMetadata_ReturnsFailure(string invalidField)
    {
        var result = LabRevision.Create(
            invalidField == "id" ? Guid.Empty : Guid.NewGuid(),
            invalidField == "course" ? Guid.Empty : Guid.NewGuid(),
            invalidField == "author" ? Guid.Empty : Guid.NewGuid(),
            invalidField == "hash" ? "invalid" : Hash);
        Assert.That(result, Is.EqualTo(new LabRevision.CreationResult.Failure(RevisionChangeOutcome.InvalidInput)));
    }

    [Test]
    public void SubmitThenArchive_RejectsInvalidTransitionWithoutChangingVersion()
    {
        var creation = LabRevision.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Hash);
        Assert.That(creation, Is.TypeOf<LabRevision.CreationResult.Success>());
        var revision = ((LabRevision.CreationResult.Success)creation).Value;
        Assert.Multiple(() =>
        {
            Assert.That(revision.Status, Is.EqualTo(LabRevisionStatus.Draft));
            Assert.That(revision.RowVersion, Is.EqualTo(1));
            Assert.That(revision.SubmitForReview(1), Is.EqualTo(RevisionChangeOutcome.Applied));
            Assert.That(revision.Archive(2), Is.EqualTo(RevisionChangeOutcome.InvalidTransition));
            Assert.That(revision.Status, Is.EqualTo(LabRevisionStatus.InReview));
            Assert.That(revision.RowVersion, Is.EqualTo(2));
        });
    }

    [Test]
    public void Decide_WithPolicyFromAnotherCourse_DoesNotChangeRevision()
    {
        var creation = LabRevision.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Hash);
        var policyCreation = CoursePublishPolicy.Create(Guid.NewGuid(), false);
        Assert.That(creation, Is.TypeOf<LabRevision.CreationResult.Success>());
        Assert.That(policyCreation, Is.TypeOf<CoursePublishPolicy.CreationResult.Success>());
        var revision = ((LabRevision.CreationResult.Success)creation).Value;
        var policy = ((CoursePublishPolicy.CreationResult.Success)policyCreation).Value;
        Assert.That(revision.SubmitForReview(1), Is.EqualTo(RevisionChangeOutcome.Applied));

        var result = revision.Decide(Guid.NewGuid(), 2, Hash, RevisionReviewDecision.Approved, policy);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(RevisionChangeOutcome.PolicyMismatch));
            Assert.That(revision.Status, Is.EqualTo(LabRevisionStatus.InReview));
            Assert.That(revision.RowVersion, Is.EqualTo(2));
        });
    }
}
