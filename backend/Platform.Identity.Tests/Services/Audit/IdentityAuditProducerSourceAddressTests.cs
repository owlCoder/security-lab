using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;

// IDN-22 TIM 09
namespace Platform.Identity.Tests.Services.Audit;

/// <summary>
/// IDN-22 / AUD-01: auth.login records the client address as sourceAddress, normalized to a plain IPv4 or
/// IPv6 address. A value that is not an address is left out and the login is still audited.
/// </summary>
[TestFixture]
[Category("IDN-22")]
public sealed class IdentityAuditProducerSourceAddressTests
{
    private const string CorrelationId = "corr-login-1";
    private const string SourceAddressDetail = "sourceAddress";

    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private Mock<ITrustedAuditWriter> _writer = null!;
    private List<AuditEventDraft> _drafts = null!;
    private IdentityAuditProducer _producer = null!;

    [SetUp]
    public void SetUp()
    {
        _drafts = [];
        _writer = new Mock<ITrustedAuditWriter>(MockBehavior.Strict);
        _writer.Setup(writer => writer.WriteAsync(
                It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEventDraft, AuditContext, CancellationToken>((draft, _, _) => _drafts.Add(draft))
            .ReturnsAsync(AuditWriteResult.Recorded(Guid.Parse("66666666-0000-4000-8000-000000000001")));

        // auth.login is never written inside a caller's transaction.
        var transactionalWriter = new Mock<ITransactionalAuditWriter>(MockBehavior.Strict);
        _producer = new IdentityAuditProducer(
            _writer.Object, transactionalWriter.Object, NullLogger<IdentityAuditProducer>.Instance);
    }

    [TestCase("203.0.113.7", "203.0.113.7")]
    [TestCase("2001:db8::1", "2001:db8::1")]
    [TestCase("::ffff:198.51.100.4", "198.51.100.4")]
    [TestCase("fe80::1%2", "fe80::1")]
    public async Task LoginSucceededAsync_ClientAddress_ShouldRecordNormalizedSourceAddress(
        string clientAddress,
        string expected)
    {
        // Arrange: SetUp gives a writer that records every draft.

        // Act
        await _producer.LoginSucceededAsync(UserId, CorrelationId, clientAddress);

        // Assert
        var details = _drafts.Single().Details;
        Assert.That(details, Does.ContainKey(SourceAddressDetail).WithValue(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-an-address")]
    [TestCase("203.0.113")]
    [TestCase("1")]
    public async Task LoginSucceededAsync_UnusableAddress_ShouldRecordLoginWithoutDetails(string? clientAddress)
    {
        // Arrange: SetUp gives a writer that records every draft.

        // Act
        var result = await _producer.LoginSucceededAsync(UserId, CorrelationId, clientAddress);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsRecorded, Is.True);
            Assert.That(_drafts.Single().Details, Is.Null);
        });
    }

    [Test]
    public async Task LoginFailedAsync_ClientAddress_ShouldRecordReasonCodeAndSourceAddress()
    {
        // Arrange
        const string clientAddress = "203.0.113.7";

        // Act
        await _producer.LoginFailedAsync("invalid_credentials", CorrelationId, sourceAddress: clientAddress);

        // Assert
        var details = _drafts.Single().Details;
        Assert.Multiple(() =>
        {
            Assert.That(details, Does.ContainKey("reasonCode").WithValue("invalid_credentials"));
            Assert.That(details, Does.ContainKey(SourceAddressDetail).WithValue(clientAddress));
        });
    }

    [Test]
    public async Task LoginFailedAsync_InvalidAddress_ShouldRecordOnlyReasonCode()
    {
        // Arrange
        const string clientAddress = "203.0.113.256";

        // Act
        await _producer.LoginFailedAsync("invalid_credentials", CorrelationId, sourceAddress: clientAddress);

        // Assert
        Assert.That(_drafts.Single().Details!.Keys, Is.EqualTo(new[] { "reasonCode" })); // A failure has a reason.
    }
}
