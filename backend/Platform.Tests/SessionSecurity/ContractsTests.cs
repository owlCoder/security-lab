using System.Reflection;
using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;

// IDN-11 IDN-22 TIM 09
namespace Platform.Tests.SessionSecurity;

[TestFixture]
public sealed class ContractsTests
{
    private const string Token = "raw-session-token-value";
    private const string TokenHash = "A1B2C3D4E5F60718293A4B5C6D7E8F90";
    private const string SourceAddress = "203.0.113.45";

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ChainId = new("11111111-2222-3333-4444-555555555555");

    [Test]
    public void RotatableSession_ToString_DoesNotContainTokenHashOrChainId()
    {
        var session = new RotatableSession(
            ChainId, "user-a", TokenHash, SessionPrivilegeLevel.Authenticated, Now, Now.AddHours(8));

        var text = session.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain(TokenHash));
            Assert.That(text, Does.Not.Contain(ChainId.ToString()));
        });
    }

    [Test]
    public void IssuedSessionToken_ToString_DoesNotContainTokenOrHash()
    {
        var text = new IssuedSessionToken(Token, TokenHash).ToString();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain(Token));
            Assert.That(text, Does.Not.Contain(TokenHash));
        });
    }

    [Test]
    public void RotateSessionRequest_ToString_DoesNotContainToken()
    {
        var request = new RotateSessionRequest(
            Token, "user-a", RotationTrigger.Login, SessionPrivilegeLevel.Authenticated, "corr-1");

        Assert.That(request.ToString(), Does.Not.Contain(Token));
    }

    [Test]
    public void SessionRotationResult_ToString_DoesNotContainNewToken()
    {
        var result = SessionRotationResult.Rotated(Token);

        Assert.That(result.ToString(), Does.Not.Contain(Token));
    }

    [Test]
    public void LoginEvent_ToString_DoesNotContainSourceAddress()
    {
        var loginEvent = new LoginEvent("user-a", Now, Succeeded: true, SourceAddress);

        Assert.That(loginEvent.ToString(), Does.Not.Contain(SourceAddress));
    }

    [Test]
    public void SessionRotationResult_Rejected_HasNoTokenAndIsNotSuccessful()
    {
        var result = SessionRotationResult.Rejected(SessionRotationCodes.SessionRevoked);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.SessionRevoked));
            Assert.That(result.NewToken, Is.Null);
        });
    }

    [Test]
    public void SessionRotationResult_Rejected_WhenCodeIsSuccessCode_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => SessionRotationResult.Rejected(SessionRotationCodes.Rotated));
    }

    [Test]
    public void LoginHistoryResult_Rejected_HasNoItemsAndIsNotSuccessful()
    {
        var result = LoginHistoryResult.Rejected(LoginHistoryCodes.NotAuthenticated);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(LoginHistoryCodes.NotAuthenticated));
            Assert.That(result.Items, Is.Empty);
        });
    }

    [Test]
    public void LoginHistoryResult_Rejected_WhenCodeIsSuccessCode_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => LoginHistoryResult.Rejected(LoginHistoryCodes.Ok));
    }

    [Test]
    public void RotatableSession_IsExpiredAt_IsTrueExactlyAtAbsoluteExpiry()
    {
        var expiresAt = Now.AddHours(8);
        var session = new RotatableSession(
            ChainId, "user-a", TokenHash, SessionPrivilegeLevel.Authenticated, Now, expiresAt);

        Assert.Multiple(() =>
        {
            Assert.That(session.IsExpiredAt(expiresAt.AddTicks(-1)), Is.False);
            Assert.That(session.IsExpiredAt(expiresAt), Is.True);
        });
    }

    [Test]
    public void RotatableSession_IsActiveAt_IsTrueOnlyWhenNotRevokedNotPendingAndNotExpired()
    {
        var active = new RotatableSession(
            ChainId, "user-a", TokenHash, SessionPrivilegeLevel.Authenticated, Now, Now.AddHours(8));

        Assert.Multiple(() =>
        {
            Assert.That(active.IsActiveAt(Now), Is.True);
            Assert.That((active with { IsPendingActivation = true }).IsActiveAt(Now), Is.False);
            Assert.That((active with { RevokedAt = Now }).IsActiveAt(Now), Is.False);
            Assert.That(active.IsActiveAt(active.AbsoluteExpiresAt), Is.False);
        });
    }

    [Test]
    public void LoginHistoryLimits_DefaultsAreWithinMaximums()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LoginHistoryLimits.DefaultRange, Is.LessThanOrEqualTo(LoginHistoryLimits.MaxRange));
            Assert.That(LoginHistoryLimits.DefaultPageSize, Is.InRange(1, LoginHistoryLimits.MaxPageSize));
            Assert.That(LoginHistoryLimits.FirstPage, Is.EqualTo(1));
        });
    }

    [Test]
    public void SearchLoginHistoryQuery_HasNoUserField()
    {
        var propertyNames = typeof(SearchLoginHistoryQuery)
            .GetProperties()
            .Select(property => property.Name.ToLowerInvariant());

        Assert.That(
            propertyNames,
            Has.None.Contains("user").And.None.Contains("subject"));
    }

    [Test]
    public void SessionSecurityModule_DoesNotReferenceOtherPlatformModules()
    {
        var referencedPlatformModules = typeof(ISessionRotationStore).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .Where(name => name.StartsWith("Platform.", StringComparison.Ordinal));

        Assert.That(referencedPlatformModules, Is.Empty);
    }

    [Test]
    public void SessionSecurityAuditEntry_HasNoTokenOrChainField()
    {
        var propertyNames = typeof(SessionSecurityAuditEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name.ToLowerInvariant());

        Assert.That(
            propertyNames,
            Has.None.Contains("token").And.None.Contains("hash").And.None.Contains("chain"));
    }
}
