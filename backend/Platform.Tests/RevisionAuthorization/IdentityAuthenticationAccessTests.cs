using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Catalog;
using Platform.Api.Security.IdentityAccess;

namespace Platform.Tests.RevisionAuthorization;

public sealed class IdentityAuthenticationAccessTests
{
    [TestCase(IdentityAccessActions.Login)]
    [TestCase(IdentityAccessActions.Logout)]
    public async Task PublicAuthentication_IsAllowedByRegisteredPolicy_WithoutSession(string action)
    {
        using var services = Services();
        var result = await services.GetRequiredService<IAccessDecision>()
            .DecideAsync(new AccessRequest(null, action, null, "public-auth"));
        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(result.PolicyId, Is.EqualTo("identity.public-authentication"));
    }

    [TestCase(IdentityAccessActions.Login)]
    [TestCase(IdentityAccessActions.Logout)]
    public async Task PublicScope_WithoutRegisteredPolicy_IsDenied(string action)
    {
        var collection = new ServiceCollection();
        collection.AddAccessControl();
        collection.AddSingleton(new InMemoryActionCatalog(
            [new ActionDefinition(action, ActionScope.Public, null, false)], freezeAfterConstruction: true));
        using var services = collection.BuildServiceProvider();
        var result = await services.GetRequiredService<IAccessDecision>()
            .DecideAsync(new AccessRequest(null, action, null, "no-policy"));
        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(result.DenialReason, Is.EqualTo(AccessDenialReason.NoPolicy));
    }

    [TestCase(IdentityAccessActions.Login)]
    [TestCase(IdentityAccessActions.Logout)]
    [TestCase(IdentityAccessActions.SessionRead)]
    public async Task AuthenticationAction_WithUnexpectedResource_IsDenied(string action)
    {
        using var services = Services();
        var result = await services.GetRequiredService<IAccessDecision>().DecideAsync(new AccessRequest(
            new SubjectRef(Guid.NewGuid().ToString(), AccountState.Active), action,
            new ResourceRef("account", Guid.NewGuid().ToString()), "unexpected-resource"));
        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(result.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task SessionRead_WithoutSubject_IsDenied()
    {
        using var services = Services();
        var result = await services.GetRequiredService<IAccessDecision>()
            .DecideAsync(new AccessRequest(null, IdentityAccessActions.SessionRead, null, "no-session"));
        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(result.DenialReason, Is.EqualTo(AccessDenialReason.Unauthenticated));
    }

    [TestCase(AccountState.Active, AccessOutcome.Allow)]
    [TestCase(AccountState.Blocked, AccessOutcome.Deny)]
    [TestCase(AccountState.Archived, AccessOutcome.Deny)]
    public async Task SessionRead_RequiresActiveSubject(AccountState state, AccessOutcome expected)
    {
        using var services = Services();
        var result = await services.GetRequiredService<IAccessDecision>().DecideAsync(new AccessRequest(
            new SubjectRef(Guid.NewGuid().ToString(), state), IdentityAccessActions.SessionRead, null, "account-state"));
        Assert.That(result.Outcome, Is.EqualTo(expected));
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddIdentityAuthenticationAccess();
        services.AddAccessControl();
        return services.BuildServiceProvider();
    }
}
