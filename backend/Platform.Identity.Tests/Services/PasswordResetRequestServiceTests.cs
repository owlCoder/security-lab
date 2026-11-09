using Microsoft.Extensions.Logging;
using Moq;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Services;

[TestFixture]
public sealed class PasswordResetRequestServiceTests
{
    [Test]
    public async Task ExistingAccount_ReturnsGenericResponseAndIssuesToken()
    {
        var accountId = Guid.NewGuid();
        var store = new Mock<ICredentialStore>(MockBehavior.Strict);
        var issuer = new Mock<IPasswordResetTokenIssuer>(MockBehavior.Strict);

        store.Setup(x => x.FindByEmailAsync(
                "existing@example.com", CancellationToken.None))
            .Returns(Task.FromResult<CredentialRecord?>(
                new CredentialRecord(accountId, AccountStatus.Active, default)));

        issuer.Setup(x => x.IssueAsync(accountId, CancellationToken.None))
            .Returns(Task.CompletedTask);

        var service = CreateService(store, issuer);
        var result = await service.RequestAsync("existing@example.com");

        Assert.That(result, Is.EqualTo(PasswordResetRequestResult.Accepted()));
        Assert.That(result.Message, Does.Not.Contain("existing@example.com"));

        store.VerifyAll();
        issuer.VerifyAll();
    }

    [Test]
    public async Task MissingAccount_ReturnsSameGenericResponse()
    {
        var store = new Mock<ICredentialStore>(MockBehavior.Strict);
        var issuer = new Mock<IPasswordResetTokenIssuer>(MockBehavior.Strict);

        store.Setup(x => x.FindByEmailAsync(
                "missing@example.com", CancellationToken.None))
            .Returns(Task.FromResult<CredentialRecord?>(default));

        var service = CreateService(store, issuer);
        var result = await service.RequestAsync("missing@example.com");

        Assert.That(result, Is.EqualTo(PasswordResetRequestResult.Accepted()));
        Assert.That(result.Message, Does.Not.Contain("missing@example.com"));

        store.VerifyAll();
        issuer.VerifyNoOtherCalls();
    }

    [TestCase("")]
    [TestCase("invalid")]
    [TestCase("example@")]
    [TestCase("spaces in@example.com")]
    public async Task InvalidEmail_IsRejectedWithoutAccountLookup(string email)
    {
        var store = new Mock<ICredentialStore>(MockBehavior.Strict);
        var issuer = new Mock<IPasswordResetTokenIssuer>(MockBehavior.Strict);

        var service = CreateService(store, issuer);
        var result = await service.RequestAsync(email);

        Assert.That(result, Is.EqualTo(PasswordResetRequestResult.InvalidEmail()));

        store.VerifyNoOtherCalls();
        issuer.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Email_IsNormalizedBeforeAccountLookup()
    {
        var store = new Mock<ICredentialStore>(MockBehavior.Strict);
        var issuer = new Mock<IPasswordResetTokenIssuer>(MockBehavior.Strict);

        store.Setup(x => x.FindByEmailAsync(
                "student@example.com", CancellationToken.None))
            .Returns(Task.FromResult<CredentialRecord?>(default));

        var service = CreateService(store, issuer);
        var result = await service.RequestAsync("  STUDENT@EXAMPLE.COM  ");

        Assert.That(result, Is.EqualTo(PasswordResetRequestResult.Accepted()));

        store.VerifyAll();
        issuer.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExcessivelyLongEmail_IsRejected()
    {
        var store = new Mock<ICredentialStore>(MockBehavior.Strict);
        var issuer = new Mock<IPasswordResetTokenIssuer>(MockBehavior.Strict);

        var service = CreateService(store, issuer);
        var email = new string('a', 250) + "@example.com";
        var result = await service.RequestAsync(email);

        Assert.That(result, Is.EqualTo(PasswordResetRequestResult.InvalidEmail()));

        store.VerifyNoOtherCalls();
        issuer.VerifyNoOtherCalls();
    }

    [TestCase(AccountStatus.Blocked)]
    [TestCase(AccountStatus.Archived)]
    public async Task InactiveAccount_ReturnsGenericResponseWithoutIssuingToken(
        AccountStatus status)
    {
        var accountId = Guid.NewGuid();
        var store = new Mock<ICredentialStore>(MockBehavior.Strict);
        var issuer = new Mock<IPasswordResetTokenIssuer>(MockBehavior.Strict);

        store.Setup(x => x.FindByEmailAsync(
                "inactive@example.com", CancellationToken.None))
            .Returns(Task.FromResult<CredentialRecord?>(
                new CredentialRecord(accountId, status, default)));

        var service = CreateService(store, issuer);
        var result = await service.RequestAsync("inactive@example.com");

        Assert.That(result, Is.EqualTo(PasswordResetRequestResult.Accepted()));
        Assert.That(result.Message, Does.Not.Contain("inactive@example.com"));

        store.VerifyAll();
        issuer.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AccountLookupFailure_ReturnsGenericResponse()
    {
        var store = new Mock<ICredentialStore>(MockBehavior.Strict);
        var issuer = new Mock<IPasswordResetTokenIssuer>(MockBehavior.Strict);

        store.Setup(x => x.FindByEmailAsync(
                "lookup@example.com", CancellationToken.None))
            .Returns(Task.FromException<CredentialRecord?>(
                new InvalidOperationException("internal lookup details")));

        var service = CreateService(store, issuer);
        var result = await service.RequestAsync("lookup@example.com");

        Assert.That(result, Is.EqualTo(PasswordResetRequestResult.Accepted()));
        Assert.That(result.Message, Does.Not.Contain("internal lookup details"));

        store.VerifyAll();
        issuer.VerifyNoOtherCalls();
    }

    [Test]
    public async Task TokenIssuanceFailure_ReturnsGenericResponse()
    {
        var accountId = Guid.NewGuid();
        var store = new Mock<ICredentialStore>(MockBehavior.Strict);
        var issuer = new Mock<IPasswordResetTokenIssuer>(MockBehavior.Strict);

        store.Setup(x => x.FindByEmailAsync(
                "existing@example.com", CancellationToken.None))
            .Returns(Task.FromResult<CredentialRecord?>(
                new CredentialRecord(accountId, AccountStatus.Active, default)));

        issuer.Setup(x => x.IssueAsync(accountId, CancellationToken.None))
            .Returns(Task.FromException(
                new InvalidOperationException("sensitive token detail")));

        var service = CreateService(store, issuer);
        var result = await service.RequestAsync("existing@example.com");

        Assert.That(result, Is.EqualTo(PasswordResetRequestResult.Accepted()));
        Assert.That(result.Message, Does.Not.Contain("sensitive token detail"));

        store.VerifyAll();
        issuer.VerifyAll();
    }

    [Test]
    public async Task TokenIssuanceFailure_DoesNotExposeSensitiveDataInLogs()
    {
        var accountId = Guid.NewGuid();
        var email = "sensitive@example.com";
        var secret = "private-reset-token-value-123";

        var store = new Mock<ICredentialStore>(MockBehavior.Strict);
        var issuer = new Mock<IPasswordResetTokenIssuer>(MockBehavior.Strict);
        var logger = new Mock<ILogger<PasswordResetRequestService>>();

        store.Setup(x => x.FindByEmailAsync(
                email, CancellationToken.None))
            .Returns(Task.FromResult<CredentialRecord?>(
                new CredentialRecord(accountId, AccountStatus.Active, default)));

        issuer.Setup(x => x.IssueAsync(accountId, CancellationToken.None))
            .Returns(Task.FromException(
                new InvalidOperationException(secret)));

        var service = new PasswordResetRequestService(
            store.Object, issuer.Object, logger.Object);

        var result = await service.RequestAsync(email);

        var loggedContent = string.Join(" ",
            logger.Invocations
                .SelectMany(invocation => invocation.Arguments)
                .Select(argument => argument?.ToString()));

        Assert.That(result, Is.EqualTo(PasswordResetRequestResult.Accepted()));
        Assert.That(logger.Invocations, Is.Not.Empty);
        Assert.That(loggedContent, Does.Not.Contain(secret));
        Assert.That(loggedContent, Does.Not.Contain(email));
        Assert.That(result.Message, Does.Not.Contain(secret));

        store.VerifyAll();
        issuer.VerifyAll();
    }

    private static PasswordResetRequestService CreateService(
        Mock<ICredentialStore> store,
        Mock<IPasswordResetTokenIssuer> issuer)
    {
        return new PasswordResetRequestService(
            store.Object,
            issuer.Object,
            Mock.Of<ILogger<PasswordResetRequestService>>());
    }
}
