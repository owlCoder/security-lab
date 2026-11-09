using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Catalog;
using Platform.Api.Security.RevisionAuthorization;


namespace Platform.Tests.RevisionAuthorization;

[TestFixture]
public sealed class RevisionDefaultDenyTests
{
    [TestCase(RevisionActions.Submit, "revision")]
    [TestCase(RevisionActions.Approve, "revision")]
    [TestCase(RevisionActions.Return, "revision")]
    
    public async Task RegisteredAction_WithoutPermission_IsDenied(
        string action, string resourceType)
    {
        var services = new ServiceCollection();

        services.AddAccessControl();
        services.AddRevisionAuthorization();
        services.AddSingleton<IRevisionResourceReader>(new FakeResourceReader());

        // Test koristi pouzdanog fake citaca umjesto baze.
        services.AddSingleton<IRevisionAuthorReader>(
            new FakeRevisionAuthorReader(Guid.NewGuid()));

        using var provider = services.BuildServiceProvider();

        provider.RegisterRevisionPolicies();

        var decisionService =
            provider.GetRequiredService<IAccessDecision>();

        var request = CreateRequest(action, resourceType);

        var result = await decisionService.DecideAsync(request);

        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(result.DenialReason,
            Is.EqualTo(AccessDenialReason.PolicyDenied));
    }

    [Test]
    public async Task UnlockAccount_WithResource_IsDeniedByAccContract()
    {
   var services = new ServiceCollection();

    services.AddAccessControl();
    services.AddRevisionAuthorization();
    services.AddSingleton<IRevisionResourceReader>(new FakeResourceReader());

    services.AddSingleton<IRevisionAuthorReader>(
    new FakeRevisionAuthorReader(Guid.NewGuid()));

using var provider = services.BuildServiceProvider();

    provider.RegisterRevisionPolicies();

    var decisionService =
        provider.GetRequiredService<IAccessDecision>();

    var request = CreateRequest(
        RevisionActions.UnlockAccount, "account");

    var result = await decisionService.DecideAsync(request);

    Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
    Assert.That(result.DenialReason,
        Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task UnknownAction_IsDenied()
    {
        var services = new ServiceCollection();
        services.AddAccessControl();

        using var provider = services.BuildServiceProvider();

        var decisionService =
            provider.GetRequiredService<IAccessDecision>();

        var request = CreateRequest("revision.delete", "revision");

        var result = await decisionService.DecideAsync(request);

        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(result.DenialReason,
            Is.EqualTo(AccessDenialReason.UnknownAction));
    }

    [Test]
    public async Task KnownActionWithoutPolicy_IsDenied()
    {
        var services = new ServiceCollection();
        services.AddAccessControl();

        using var provider = services.BuildServiceProvider();

        var catalog =
            provider.GetRequiredService<InMemoryActionCatalog>();

        catalog.Register(new ActionDefinition(
            RevisionActions.Submit,
            ActionScope.Course,
            "revision",
            false));

        var decisionService =
            provider.GetRequiredService<IAccessDecision>();

        var request = CreateRequest(
            RevisionActions.Submit, "revision");

        var result = await decisionService.DecideAsync(request);

        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(result.DenialReason,
            Is.EqualTo(AccessDenialReason.NoPolicy));
    }

    private static AccessRequest CreateRequest(
        string action, string resourceType)
    {
        return new AccessRequest(
            new SubjectRef(
                Guid.NewGuid().ToString(),
                AccountState.Active),
            action,
            new ResourceRef(
                resourceType,
                Guid.NewGuid().ToString()),
            "test-correlation");
    }

    private sealed class FakeRevisionAuthorReader(Guid authorId)
        : IRevisionAuthorReader
    {
        public Task<Guid?> FindAuthorIdAsync(
            Guid revisionId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Guid?>(authorId);
        }
    }

    private sealed class FakeResourceReader : IRevisionResourceReader
    {
        public Task<RevisionResource?> FindAsync(Guid revisionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RevisionResource?>(new RevisionResource(Guid.NewGuid(), Guid.NewGuid()));
    }
}
