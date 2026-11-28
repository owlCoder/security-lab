using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using static Platform.Audit.Tests.AuditDraftExamples;

// IDN-22 TIM 09

namespace Platform.Audit.Tests;

// IDN-22 (AUD-19): auth.login may carry the full source address; anything that is not
// a plain IPv4 or IPv6 address is rejected. Synthetic documentation ranges only.
[TestFixture]
public sealed class LoginSourceAddressValidationTests
{
    private readonly AuditDraftValidator _validator = new();

    [TestCase("203.0.113.7")]
    [TestCase("192.0.2.1")]
    [TestCase("2001:db8::1")]
    [TestCase("2001:db8:85a3:0:0:8a2e:370:7334")]
    [TestCase("::ffff:198.51.100.4")]
    public void Validate_WhenSuccessfulLoginHasSourceAddress_AcceptsIt(string sourceAddress)
    {
        var draft = SuccessfulLogin(sourceAddress);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.None));
    }

    [Test]
    public void Validate_WhenFailedAnonymousLoginHasSourceAddress_AcceptsIt()
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.AuthLogin,
            AuditOutcome.Failure,
            Resource: TargetAccount,
            Details: Details(("reasonCode", "invalid_credentials"), ("sourceAddress", "198.51.100.23")));

        Assert.That(_validator.Validate(draft, AnonymousContext), Is.EqualTo(AuditRejectionCode.None));
    }

    [TestCase("not-an-address")]
    [TestCase("localhost")]
    [TestCase("1")]
    [TestCase("203.0.113")]
    [TestCase("203.0.113.256")]
    [TestCase("fe80::1%2")]
    [TestCase("[2001:db8::1]")]
    [TestCase("203.0.113.7:443")]
    [TestCase(" 203.0.113.7")]
    [TestCase("203.0.113.7\n")]
    [TestCase("")]
    public void Validate_WhenSourceAddressIsNotAPlainIpAddress_RejectsValue(string sourceAddress)
    {
        var draft = SuccessfulLogin(sourceAddress);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [Test]
    public void Validate_WhenSourceAddressIsLongerThanAnIpAddress_RejectsValue()
    {
        var draft = SuccessfulLogin("2001:0db8:0000:0000:0000:0000:0000:0001:0001:0001");

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    private static AuditEventDraft SuccessfulLogin(string sourceAddress) =>
        new(
            AuditEventTypes.AuthLogin,
            AuditOutcome.Success,
            Resource: TargetAccount,
            Details: Details(("sourceAddress", sourceAddress)));
}
