using NUnit.Framework;
using Platform.Identity.Models;

namespace Platform.Identity.Tests;

[TestFixture]
public sealed class BackupCodeRecordTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private const string ValidCodeHash =
        "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Test]
    public void TryCreate_WithValidData_CreatesRecord()
    {
        Guid userId = Guid.NewGuid();

        bool created = BackupCodeRecord.TryCreate(
            userId,
            ValidCodeHash,
            CreatedAt,
            out BackupCodeRecord record);

        Assert.That(created, Is.True);
        Assert.That(record.IsValid(), Is.True);
        Assert.That(record.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(record.UserId, Is.EqualTo(userId));
        Assert.That(record.CodeHash, Is.EqualTo(ValidCodeHash));
        Assert.That(record.CreatedAt, Is.EqualTo(CreatedAt));
        Assert.That(record.IsUsed, Is.False);
        Assert.That(record.UsedAt, Is.EqualTo(default(DateTimeOffset)));
    }

    [Test]
    public void TryCreate_WithEmptyUserId_ReturnsFalse()
    {
        bool created = BackupCodeRecord.TryCreate(
            Guid.Empty,
            ValidCodeHash,
            CreatedAt,
            out _);

        Assert.That(created, Is.False);
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("   ")]
    public void TryCreate_WithInvalidCodeHash_ReturnsFalse(
        string codeHash)
    {
        bool created = BackupCodeRecord.TryCreate(
            Guid.NewGuid(),
            codeHash,
            CreatedAt,
            out _);

        Assert.That(created, Is.False);
    }

    [Test]
    public void TryCreate_WithDefaultCreatedAt_ReturnsFalse()
    {
        bool created = BackupCodeRecord.TryCreate(
            Guid.NewGuid(),
            ValidCodeHash,
            default,
            out _);

        Assert.That(created, Is.False);
    }

    [Test]
    public void CanBeUsed_BeforeCreationTime_ReturnsFalse()
    {
        BackupCodeRecord.TryCreate(
            Guid.NewGuid(),
            ValidCodeHash,
            CreatedAt,
            out BackupCodeRecord record);

        bool canBeUsed =
            record.CanBeUsed(CreatedAt.AddSeconds(-1));

        Assert.That(canBeUsed, Is.False);
    }

    [Test]
    public void CanBeUsed_AfterCreationTime_ReturnsTrue()
    {
        BackupCodeRecord.TryCreate(
            Guid.NewGuid(),
            ValidCodeHash,
            CreatedAt,
            out BackupCodeRecord record);

        bool canBeUsed =
            record.CanBeUsed(CreatedAt.AddMinutes(1));

        Assert.That(canBeUsed, Is.True);
    }

    [Test]
    public void TryMarkAsUsed_FirstAttempt_Succeeds()
    {
        BackupCodeRecord.TryCreate(
            Guid.NewGuid(),
            ValidCodeHash,
            CreatedAt,
            out BackupCodeRecord record);

        DateTimeOffset usedAt = CreatedAt.AddMinutes(5);

        bool result = record.TryMarkAsUsed(usedAt);

        Assert.That(result, Is.True);
        Assert.That(record.IsUsed, Is.True);
        Assert.That(record.UsedAt, Is.EqualTo(usedAt));
        Assert.That(record.CanBeUsed(usedAt), Is.False);
    }

    [Test]
    public void TryMarkAsUsed_SecondAttempt_ReturnsFalse()
    {
        BackupCodeRecord.TryCreate(
            Guid.NewGuid(),
            ValidCodeHash,
            CreatedAt,
            out BackupCodeRecord record);

        DateTimeOffset firstUse = CreatedAt.AddMinutes(5);
        DateTimeOffset secondUse = CreatedAt.AddMinutes(10);

        bool firstResult = record.TryMarkAsUsed(firstUse);
        bool secondResult = record.TryMarkAsUsed(secondUse);

        Assert.That(firstResult, Is.True);
        Assert.That(secondResult, Is.False);
        Assert.That(record.IsUsed, Is.True);
        Assert.That(record.UsedAt, Is.EqualTo(firstUse));
    }

    [Test]
    public void TryMarkAsUsed_BeforeCreation_DoesNotChangeState()
    {
        BackupCodeRecord.TryCreate(
            Guid.NewGuid(),
            ValidCodeHash,
            CreatedAt,
            out BackupCodeRecord record);

        bool result =
            record.TryMarkAsUsed(CreatedAt.AddSeconds(-1));

        Assert.That(result, Is.False);
        Assert.That(record.IsUsed, Is.False);
        Assert.That(
            record.UsedAt,
            Is.EqualTo(default(DateTimeOffset)));
    }

    [Test]
    public void DifferentRecords_HaveIndependentUsageStates()
    {
        Guid userId = Guid.NewGuid();

        BackupCodeRecord.TryCreate(
            userId,
            ValidCodeHash,
            CreatedAt,
            out BackupCodeRecord firstRecord);

        BackupCodeRecord.TryCreate(
            userId,
            ValidCodeHash,
            CreatedAt,
            out BackupCodeRecord secondRecord);

        firstRecord.TryMarkAsUsed(CreatedAt.AddMinutes(1));

        Assert.That(firstRecord.Id, Is.Not.EqualTo(secondRecord.Id));
        Assert.That(firstRecord.IsUsed, Is.True);
        Assert.That(secondRecord.IsUsed, Is.False);
    }
    [Test]
    public void TryCreate_WithPlaintextCode_ReturnsFalse()
    {
        bool created = BackupCodeRecord.TryCreate(
            Guid.NewGuid(),
            "123456",
            CreatedAt,
            out _);

        Assert.That(created, Is.False);
    }

    [Test]
    public void TryCreate_WithNonHexadecimalHash_ReturnsFalse()
    {
        string invalidHash = new string('z', 64);

        bool created = BackupCodeRecord.TryCreate(
            Guid.NewGuid(),
            invalidHash,
            CreatedAt,
            out _);

        Assert.That(created, Is.False);
    }

}
