using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Security.RevisionAuthorization;
using Platform.Audit.Contracts;

namespace Platform.Tests.RevisionAuthorization;

[TestFixture]
public sealed class RevisionAuditContractTests
{
    [TestCase(RevisionActions.Submit)]
    [TestCase(RevisionActions.Approve)]
    [TestCase(RevisionActions.Return)]
    [TestCase(RevisionActions.UnlockAccount)]
    [TestCase(RevisionActions.Entry)]
    public void DeniedEvent_ConformsToAuditCatalog(string action)
    {
        var request = new AccessRequest(
            new SubjectRef(Guid.NewGuid().ToString(), AccountState.Active),
            action,
            action == RevisionActions.Entry ? null : new ResourceRef("revision", Guid.NewGuid().ToString()),
            "test-correlation");

        var decision = AccessDecision.Deny(
            AccessDenialReason.PolicyError,
            request.CorrelationId,
            "v1");

        var draft = RevisionAuditEventFactory.CreateDeniedEvent(
            request, decision);

        Assert.That(draft, Is.Not.Null);
        Assert.That(
            AuditEventCatalog.TryGet(draft!.EventType, out var schema),
            Is.True);

        Assert.That(schema!.ActorsByOutcome.ContainsKey(draft.Outcome), Is.True);

        foreach (var key in draft.Details!.Keys)
        {
            Assert.That(schema.Details.ContainsKey(key), Is.True);
        }

        Assert.That(draft.CourseId, Is.Null);
        Assert.That(draft.Resource, Is.Null);
    }
}
