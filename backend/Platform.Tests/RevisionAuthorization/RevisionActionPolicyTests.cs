using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Security.RevisionAuthorization;

namespace Platform.Tests.RevisionAuthorization;

[TestFixture]
public sealed class RevisionActionPolicyTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ResourceId = Guid.NewGuid();

    private static AccessRequest CreateRequest(
        string action,
        string resourceType,
        AccountState state = AccountState.Active)
    {
        return new AccessRequest(
            new SubjectRef(UserId.ToString(), state),
            action,
            action == RevisionActions.UnlockAccount ? null : new ResourceRef(resourceType, ResourceId.ToString()),
            "test-correlation");
    }

    [TestCase(RevisionActions.Submit, "revision")]
    [TestCase(RevisionActions.Return, "revision")]
    [TestCase(RevisionActions.UnlockAccount, "account")]
    public async Task ExplicitPermission_AllowsAction(
        string action, string resourceType)
    {
        var policy = new RevisionActionPolicy(
            new FakePermission(true));

        var result = await policy.EvaluateAsync(
            CreateRequest(action, resourceType));

        Assert.That(result, Is.True);
    }

    [TestCase(RevisionActions.Submit, "revision")]
    [TestCase(RevisionActions.Return, "revision")]
    [TestCase(RevisionActions.UnlockAccount, "account")]
    public async Task MissingPermission_DeniesAction(
        string action, string resourceType)
    {
        var policy = new RevisionActionPolicy(
            new FakePermission(false));

        var result = await policy.EvaluateAsync(
            CreateRequest(action, resourceType));

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UnknownAction_IsDenied()
    {
        var policy = new RevisionActionPolicy(
            new FakePermission(true));

        var result = await policy.EvaluateAsync(
            CreateRequest("revision.delete", "revision"));

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task WrongResourceType_IsDenied()
    {
        var policy = new RevisionActionPolicy(
            new FakePermission(true));

        var result = await policy.EvaluateAsync(
            CreateRequest(RevisionActions.Submit, "account"));

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task BlockedUser_IsDenied()
    {
        var policy = new RevisionActionPolicy(
            new FakePermission(true));

        var result = await policy.EvaluateAsync(
            CreateRequest(
                RevisionActions.Return,
                "revision",
                AccountState.Blocked));

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task MissingSubject_IsDenied()
    {
        var policy = new RevisionActionPolicy(
            new FakePermission(true));

        var request = new AccessRequest(
            null,
            RevisionActions.Submit,
            new ResourceRef("revision", ResourceId.ToString()),
            "test-correlation");

        var result = await policy.EvaluateAsync(request);

        Assert.That(result, Is.False);
    }

    private sealed class FakePermission(bool allowed)
        : IRevisionActionPermission
    {
        public ValueTask<bool> IsAllowedAsync(
            Guid userId,
            string action,
            Guid resourceId,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(allowed);
        }
    }
}
