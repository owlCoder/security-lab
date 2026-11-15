using Moq;
using NUnit.Framework;
using Platform.Identity.Interfaces;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("IDN-09")]
public sealed class BackupCodeRedemptionServiceTests
{
    private const string ValidCode =
        "01234567-89ABCDEF-01234567-89ABCDEF";

    private const string OtherCode =
        "11234567-89ABCDEF-01234567-89ABCDEF";

    private static readonly Guid UserId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Guid OtherUserId =
        Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static BackupCodeRedemptionService CreateService(
        Mock<IBackupCodeConsumption> consumption)
    {
        return new BackupCodeRedemptionService(
            new BackupCodeGenerator(),
            consumption.Object,
            TimeProvider.System);
    }

    private static string ComputeHash(string code)
    {
        var generator = new BackupCodeGenerator();

        bool success = generator.TryComputeHash(
            code, out string codeHash);

        Assert.That(success, Is.True);

        return codeHash;
    }

    [Test]
    public async Task RedeemAsync_ValidCode_ReturnsSuccess()
    {
        var consumption = new Mock<IBackupCodeConsumption>();
        string codeHash = ComputeHash(ValidCode);

        consumption
            .Setup(x => x.TryConsumeAsync(
                UserId,
                codeHash,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService(consumption);

        var result = await service.RedeemAsync(
            UserId, ValidCode);

        Assert.That(result.IsSuccessful, Is.True);

        consumption.Verify(x => x.TryConsumeAsync(
            UserId,
            codeHash,
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("")]
    [TestCase("invalid")]
    [TestCase("123456")]
    [TestCase("01234567-89ABCDEF-01234567-89ABCDEG")]
    public async Task RedeemAsync_InvalidFormat_ReturnsRejected(
        string invalidCode)
    {
        var consumption = new Mock<IBackupCodeConsumption>();
        var service = CreateService(consumption);

        var result = await service.RedeemAsync(
            UserId, invalidCode);

        Assert.That(result.IsSuccessful, Is.False);

        consumption.Verify(x => x.TryConsumeAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task RedeemAsync_EmptyUserId_ReturnsRejected()
    {
        var consumption = new Mock<IBackupCodeConsumption>();
        var service = CreateService(consumption);

        var result = await service.RedeemAsync(
            Guid.Empty, ValidCode);

        Assert.That(result.IsSuccessful, Is.False);

        consumption.VerifyNoOtherCalls();
    }

    [Test]
    public async Task RedeemAsync_WrongCode_ReturnsRejected()
    {
        var consumption = new Mock<IBackupCodeConsumption>();

        string validHash = ComputeHash(ValidCode);
        string otherHash = ComputeHash(OtherCode);

        consumption
            .Setup(x => x.TryConsumeAsync(
                UserId,
                validHash,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        consumption
            .Setup(x => x.TryConsumeAsync(
                UserId,
                otherHash,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService(consumption);

        var result = await service.RedeemAsync(
            UserId, OtherCode);

        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(validHash, Is.Not.EqualTo(otherHash));
    }

    [Test]
    public async Task RedeemAsync_CodeOfAnotherUser_ReturnsRejected()
    {
        var consumption = new Mock<IBackupCodeConsumption>();
        string codeHash = ComputeHash(ValidCode);

        consumption
            .Setup(x => x.TryConsumeAsync(
                OtherUserId,
                codeHash,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService(consumption);

        var result = await service.RedeemAsync(
            OtherUserId, ValidCode);

        Assert.That(result.IsSuccessful, Is.False);

        consumption.Verify(x => x.TryConsumeAsync(
            OtherUserId,
            codeHash,
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RedeemAsync_AlreadyUsedCode_ReturnsRejected()
    {
        var consumption = new Mock<IBackupCodeConsumption>();
        string codeHash = ComputeHash(ValidCode);

        consumption
            .Setup(x => x.TryConsumeAsync(
                UserId,
                codeHash,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService(consumption);

        var result = await service.RedeemAsync(
            UserId, ValidCode);

        Assert.That(result.IsSuccessful, Is.False);
    }

    [Test]
    public async Task RedeemAsync_ReusedCode_ReturnsRejected()
    {
        var consumption = new Mock<IBackupCodeConsumption>();
        string codeHash = ComputeHash(ValidCode);

        consumption
            .SetupSequence(x => x.TryConsumeAsync(
                UserId,
                codeHash,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        var service = CreateService(consumption);

        var firstResult = await service.RedeemAsync(
            UserId, ValidCode);

        var secondResult = await service.RedeemAsync(
            UserId, ValidCode);

        Assert.That(firstResult.IsSuccessful, Is.True);
        Assert.That(secondResult.IsSuccessful, Is.False);

        consumption.Verify(x => x.TryConsumeAsync(
            UserId,
            codeHash,
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task RedeemAsync_RejectionDoesNotExposeCode()
    {
        var consumption = new Mock<IBackupCodeConsumption>();
        var service = CreateService(consumption);

        var result = await service.RedeemAsync(
            UserId, "invalid");

        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(result.Message, Does.Not.Contain("invalid"));
        Assert.That(result.Message, Is.Not.Empty);
    }
}
