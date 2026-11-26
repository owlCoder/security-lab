using NUnit.Framework;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Grants;

namespace Platform.Access.Tests.Grants;

[TestFixture]
[Property("RequirementId", "ACC-08-05")]
public sealed class ElevatedGrantValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Action = AccessActions.ResultsSensitiveRead.Id;

    private readonly Guid holderId = Guid.NewGuid();
    private readonly Guid issuerId = Guid.NewGuid();
    private readonly Guid courseId = Guid.NewGuid();
    private readonly ElevatedGrantValidator validator = new();

    private ResourceDescriptor Resource() =>
        new("attempt", "attempt-9", courseId, Guid.NewGuid(), null, new Dictionary<string, AttributeValue>());

    private ElevatedGrant CourseGrant(
        Guid? holder = null,
        Guid? issuer = null,
        string? action = null,
        string? scopeId = null,
        string reason = "incident review",
        DateTimeOffset? notBefore = null,
        DateTimeOffset? expiresAt = null,
        bool revoked = false) =>
        new(
            Guid.NewGuid(),
            holder ?? holderId,
            issuer ?? issuerId,
            action ?? Action,
            new GrantScope(GrantScopeKind.Course, scopeId ?? courseId.ToString("D")),
            reason,
            notBefore ?? Now.AddHours(-1),
            expiresAt ?? Now.AddHours(1),
            revoked);

    private bool IsValid(ElevatedGrant grant, DateTimeOffset? now = null) =>
        validator.IsValid(grant, holderId, Action, Resource(), now ?? Now);

    [Test]
    public void ValidCourseGrant_IsAccepted() =>
        Assert.That(IsValid(CourseGrant()), Is.True);

    [Test]
    public void ValidResourceGrant_IsAccepted()
    {
        var grant = CourseGrant() with { Scope = new GrantScope(GrantScopeKind.Resource, "attempt-9") };

        Assert.That(IsValid(grant), Is.True);
    }

    [Test]
    public void ExpiredGrant_IsRejectedExactlyAtExpiry()
    {
        var grant = CourseGrant(expiresAt: Now);

        Assert.That(IsValid(grant), Is.False);
        Assert.That(IsValid(grant, Now.AddSeconds(-1)), Is.True);
    }

    [Test]
    public void GrantNotYetActive_IsRejectedUntilNotBefore()
    {
        var grant = CourseGrant(notBefore: Now.AddSeconds(1));

        Assert.That(IsValid(grant), Is.False);
        Assert.That(IsValid(grant, Now.AddSeconds(1)), Is.True);
    }

    [Test]
    public void RevokedGrant_IsRejected() =>
        Assert.That(IsValid(CourseGrant(revoked: true)), Is.False);

    [Test]
    public void GrantOfAnotherHolder_IsRejected() =>
        Assert.That(IsValid(CourseGrant(holder: Guid.NewGuid())), Is.False);

    [Test]
    public void GrantForAnotherAction_IsRejected() =>
        Assert.That(IsValid(CourseGrant(action: "attempt.read")), Is.False);

    [Test]
    public void GrantForAnotherCourse_IsRejected() =>
        Assert.That(IsValid(CourseGrant(scopeId: Guid.NewGuid().ToString("D"))), Is.False);

    [Test]
    public void GrantForAnotherResource_IsRejected()
    {
        var grant = CourseGrant() with { Scope = new GrantScope(GrantScopeKind.Resource, "attempt-10") };

        Assert.That(IsValid(grant), Is.False);
    }

    [Test]
    public void CourseGrantWithMalformedScopeId_IsRejected() =>
        Assert.That(IsValid(CourseGrant(scopeId: "not-a-guid")), Is.False);

    [Test]
    public void CourseGrant_IsRejectedWhenResourceHasNoCourse()
    {
        var resource = Resource() with { CourseId = null };

        Assert.That(validator.IsValid(CourseGrant(), holderId, Action, resource, Now), Is.False);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void GrantWithoutReason_IsRejected(string reason) =>
        Assert.That(IsValid(CourseGrant(reason: reason)), Is.False);

    [Test]
    public void SelfIssuedGrant_IsRejected() =>
        Assert.That(IsValid(CourseGrant(issuer: holderId)), Is.False);
}
