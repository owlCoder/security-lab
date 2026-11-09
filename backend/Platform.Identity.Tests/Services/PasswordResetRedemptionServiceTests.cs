using Microsoft.Extensions.Logging;
using Moq;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Services;

[TestFixture]
public sealed class PasswordResetRedemptionServiceTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task Redemption_ReturnsResultFromConsumption(
        bool consumptionSucceeded)
    {
        var token = new string('A', 43);
        var tokenHash = new string('B', 64);

        var hasher = new Mock<IPasswordResetTokenHasher>(
            MockBehavior.Strict);

        var consumption = new Mock<IPasswordResetTokenConsumption>(
            MockBehavior.Strict);

        hasher.Setup(x => x.TryComputeHash(
                token, out tokenHash))
            .Returns(true);
        
        var now = DateTimeOffset.UtcNow;

        consumption.Setup(x => x.GetStateAsync(
                tokenHash, CancellationToken.None))
            .ReturnsAsync(new PasswordResetTokenState(
                true,
                now.AddMinutes(-5),
                now.AddMinutes(5),
                false));

        consumption.Setup(x => x.TryConsumeAsync(
                tokenHash,
                It.IsAny<DateTimeOffset>(),
                CancellationToken.None))
            .ReturnsAsync(consumptionSucceeded);

        var service = CreateService(hasher, consumption);
        var result = await service.RedeemAsync(token);

        Assert.That(result.IsSuccessful,
            Is.EqualTo(consumptionSucceeded));

        hasher.VerifyAll();
        consumption.VerifyAll();
    }

    [Test]
    public async Task EmptyToken_IsRejected()
    {
        var hasher = new Mock<IPasswordResetTokenHasher>(
            MockBehavior.Strict);

        var consumption = new Mock<IPasswordResetTokenConsumption>(
            MockBehavior.Strict);

        var service = CreateService(hasher, consumption);
        var result = await service.RedeemAsync(string.Empty);

        Assert.That(result,
            Is.EqualTo(PasswordResetRedemptionResult.Rejected()));

        hasher.VerifyNoOtherCalls();
        consumption.VerifyNoOtherCalls();
    }

    [Test]
    public async Task InvalidTokenHash_IsRejected()
    {
        var token = "invalid-token";
        var tokenHash = string.Empty;

        var hasher = new Mock<IPasswordResetTokenHasher>(
            MockBehavior.Strict);

        var consumption = new Mock<IPasswordResetTokenConsumption>(
            MockBehavior.Strict);

        hasher.Setup(x => x.TryComputeHash(
                token, out tokenHash))
            .Returns(false);

        var service = CreateService(hasher, consumption);
        var result = await service.RedeemAsync(token);

        Assert.That(result,
            Is.EqualTo(PasswordResetRedemptionResult.Rejected()));

        hasher.VerifyAll();
        consumption.VerifyNoOtherCalls();
    }

    [TestCase(false, false, -5, 5)]
    [TestCase(true, false, -10, -1)]
    [TestCase(true, true, -5, 5)]
    [TestCase(true, false, 5, 10)]
    [TestCase(true, false, -5, -5)]
    public async Task InvalidTokenState_IsRejectedWithoutConsumption(
        bool exists,
        bool isUsed,
        int createdOffsetMinutes,
        int expiresOffsetMinutes)
    {
        var token = new string('A', 43);
        var tokenHash = new string('B', 64);
        var now = DateTimeOffset.UtcNow;

        var hasher = new Mock<IPasswordResetTokenHasher>(
            MockBehavior.Strict);

        var consumption = new Mock<IPasswordResetTokenConsumption>(
            MockBehavior.Strict);

        hasher.Setup(x => x.TryComputeHash(
                token, out tokenHash))
            .Returns(true);

        consumption.Setup(x => x.GetStateAsync(
                tokenHash, CancellationToken.None))
            .ReturnsAsync(new PasswordResetTokenState(
                exists,
                now.AddMinutes(createdOffsetMinutes),
                now.AddMinutes(expiresOffsetMinutes),
                isUsed));

        var service = CreateService(hasher, consumption);

        var result = await service.RedeemAsync(token);

        Assert.That(
            result,
            Is.EqualTo(PasswordResetRedemptionResult.Rejected()));

        hasher.VerifyAll();
        consumption.VerifyAll();
        consumption.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ReusedToken_IsRejectedAfterFirstConsumption()
    {
        var token = new string('A', 43);
        var tokenHash = new string('B', 64);
        var now = DateTimeOffset.UtcNow;

        var hasher = new Mock<IPasswordResetTokenHasher>(
            MockBehavior.Strict);

        var consumption = new Mock<IPasswordResetTokenConsumption>(
            MockBehavior.Strict);

        hasher.Setup(x => x.TryComputeHash(
                token, out tokenHash))
            .Returns(true);

        consumption.SetupSequence(x => x.GetStateAsync(
                tokenHash, CancellationToken.None))
            .ReturnsAsync(new PasswordResetTokenState(
                true,
                now.AddMinutes(-5),
                now.AddMinutes(5),
                false))
            .ReturnsAsync(new PasswordResetTokenState(
                true,
                now.AddMinutes(-5),
                now.AddMinutes(5),
                true));

        consumption.Setup(x => x.TryConsumeAsync(
                tokenHash,
                It.Is<DateTimeOffset>(time =>
                    time >= now && time < now.AddMinutes(1)),
                CancellationToken.None))
            .ReturnsAsync(true);

        var service = CreateService(hasher, consumption);

        var firstResult = await service.RedeemAsync(token);
        var secondResult = await service.RedeemAsync(token);

        Assert.That(firstResult,
            Is.EqualTo(PasswordResetRedemptionResult.Success()));

        Assert.That(secondResult,
            Is.EqualTo(PasswordResetRedemptionResult.Rejected()));

        consumption.Verify(x => x.GetStateAsync(
            tokenHash, CancellationToken.None), Times.Exactly(2));

        consumption.Verify(x => x.TryConsumeAsync(
            tokenHash,
            It.Is<DateTimeOffset>(time =>
                time >= now && time < now.AddMinutes(1)),
            CancellationToken.None), Times.Once);

        hasher.VerifyAll();
        consumption.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ConsumptionFailure_DoesNotExposeSensitiveData()
    {
        var token = new string('A', 43);
        var tokenHash = new string('B', 64);
        var sensitiveDetail = $"Internal failure for token: {token}";
        var now = DateTimeOffset.UtcNow;

        var hasher = new Mock<IPasswordResetTokenHasher>(
            MockBehavior.Strict);

        var consumption = new Mock<IPasswordResetTokenConsumption>(
            MockBehavior.Strict);

        var logger = new Mock<ILogger<PasswordResetRedemptionService>>();

        hasher.Setup(x => x.TryComputeHash(
                token, out tokenHash))
            .Returns(true);

        consumption.Setup(x => x.GetStateAsync(
                tokenHash, CancellationToken.None))
            .ReturnsAsync(new PasswordResetTokenState(
                true,
                now.AddMinutes(-5),
                now.AddMinutes(5),
                false));

        consumption.Setup(x => x.TryConsumeAsync(
                tokenHash,
                It.Is<DateTimeOffset>(time =>
                    time >= now && time < now.AddMinutes(1)),
                CancellationToken.None))
            .Returns(Task.FromException<bool>(
                new InvalidOperationException(sensitiveDetail)));

        var service = new PasswordResetRedemptionService(
            hasher.Object,
            consumption.Object,
            TimeProvider.System,
            logger.Object);

        var result = await service.RedeemAsync(token);

        var loggedContent = string.Join(" ",
            logger.Invocations
                .SelectMany(call => call.Arguments)
                .Select(argument => argument?.ToString()));

        Assert.That(result,
            Is.EqualTo(PasswordResetRedemptionResult.Rejected()));

        Assert.That(logger.Invocations, Is.Not.Empty);

        Assert.That(loggedContent,
            Does.Not.Contain(sensitiveDetail));

        Assert.That(loggedContent,
            Does.Not.Contain(token));

        Assert.That(result.Message,
            Does.Not.Contain(token));

        hasher.VerifyAll();
        consumption.VerifyAll();
    }

    private static PasswordResetRedemptionService CreateService(
        Mock<IPasswordResetTokenHasher> hasher,
        Mock<IPasswordResetTokenConsumption> consumption)
    {
        return new PasswordResetRedemptionService(
            hasher.Object,
            consumption.Object,
            TimeProvider.System,
            Mock.Of<ILogger<PasswordResetRedemptionService>>());
    }
}
