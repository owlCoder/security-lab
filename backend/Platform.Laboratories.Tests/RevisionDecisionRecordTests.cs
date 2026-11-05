using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Models;

namespace Platform.Laboratories.Tests;

public class RevisionDecisionRecordTests
{
    [TestCase(RevisionReviewDecision.Approved)]
    [TestCase(RevisionReviewDecision.ReturnedForChanges)]
    public void Create_WithValidData_PreservesDecisionAndPolicy(RevisionReviewDecision decision)
    {
        var id = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(1));
        var result = RevisionDecisionRecord.Create(
            id, revisionId, reviewerId, decision, time, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", true);

        Assert.That(result, Is.TypeOf<RevisionDecisionRecord.CreationResult.Success>());
        var value = ((RevisionDecisionRecord.CreationResult.Success)result).Value;
        Assert.Multiple(() =>
        {
            Assert.That(value.Id, Is.EqualTo(id));
            Assert.That(value.RevisionId, Is.EqualTo(revisionId));
            Assert.That(value.ReviewerId, Is.EqualTo(reviewerId));
            Assert.That(value.Decision, Is.EqualTo(decision));
            Assert.That(value.DecidedAt, Is.EqualTo(time));
            Assert.That(value.DecidedAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(value.ContentHash, Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
            Assert.That(value.RequiresIndependentApproval, Is.True);
        });
    }

    [TestCase("id", RevisionDecisionRecord.CreationError.InvalidId)]
    [TestCase("revision", RevisionDecisionRecord.CreationError.InvalidRevisionId)]
    [TestCase("reviewer", RevisionDecisionRecord.CreationError.InvalidReviewerId)]
    [TestCase("unspecified", RevisionDecisionRecord.CreationError.InvalidDecision)]
    [TestCase("unknown", RevisionDecisionRecord.CreationError.InvalidDecision)]
    [TestCase("time", RevisionDecisionRecord.CreationError.InvalidDecisionTime)]
    [TestCase("hash", RevisionDecisionRecord.CreationError.InvalidContentHash)]
    public void Create_WithInvalidData_ReturnsSpecificFailure(
        string invalidField, RevisionDecisionRecord.CreationError expected)
    {
        var result = RevisionDecisionRecord.Create(
            invalidField == "id" ? Guid.Empty : Guid.NewGuid(),
            invalidField == "revision" ? Guid.Empty : Guid.NewGuid(),
            invalidField == "reviewer" ? Guid.Empty : Guid.NewGuid(),
            invalidField == "unspecified" ? RevisionReviewDecision.NotSpecified :
                invalidField == "unknown" ? (RevisionReviewDecision)99 : RevisionReviewDecision.Approved,
            invalidField == "time" ? default : DateTimeOffset.UtcNow,
            invalidField == "hash" ? " " : "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            true);

        Assert.That(result, Is.EqualTo(new RevisionDecisionRecord.CreationResult.Failure(expected)));
    }
}
