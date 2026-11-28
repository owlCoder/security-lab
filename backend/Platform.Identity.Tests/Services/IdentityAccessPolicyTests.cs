using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.ValueObjects;
using Platform.Api.Security.IdentityAccess;

namespace Platform.Identity.Tests.Services;

[TestFixture]
public sealed class IdentityAccessPolicyTests
{
    private const string CorrelationId = "identity-policy-test";

    [Test]
    public void IdentityAccessActions_HaveValidIds()
    {
        Assert.That(ActionId.IsValid(IdentityAccessActions.EmailConfirmation), Is.True);
        Assert.That(ActionId.IsValid(IdentityAccessActions.SessionNotificationsRead), Is.True);
        Assert.That(ActionId.IsValid(IdentityAccessActions.SessionNotificationsUpdate), Is.True);
    }

    [Test]
    public async Task PublicAuthenticationPolicy_AllowsEmailConfirmation()
    {
        var policy = new PublicAuthenticationPolicy();
        var request = new AccessRequest(
            null,
            IdentityAccessActions.EmailConfirmation,
            null,
            CorrelationId);

        Assert.That(await policy.EvaluateAsync(request), Is.True);
    }

    [Test]
    public async Task SessionNotificationPolicy_AllowsActiveOwnerActionsOnly()
    {
        var policy = new SessionNotificationPolicy();
        var owner = new SubjectRef(Guid.NewGuid().ToString("D"), AccountState.Active);

        foreach (var action in new[]
                 {
                     IdentityAccessActions.SessionNotificationsRead,
                     IdentityAccessActions.SessionNotificationsUpdate
                 })
        {
            var request = new AccessRequest(owner, action, null, CorrelationId);
            Assert.That(await policy.EvaluateAsync(request), Is.True);
        }

        var anonymous = new AccessRequest(
            null,
            IdentityAccessActions.SessionNotificationsRead,
            null,
            CorrelationId);
        var inactive = new AccessRequest(
            new SubjectRef(owner.Id, AccountState.Blocked),
            IdentityAccessActions.SessionNotificationsRead,
            null,
            CorrelationId);

        Assert.That(await policy.EvaluateAsync(anonymous), Is.False);
        Assert.That(await policy.EvaluateAsync(inactive), Is.False);
    }
}
