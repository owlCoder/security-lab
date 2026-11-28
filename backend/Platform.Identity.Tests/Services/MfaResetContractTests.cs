using NUnit.Framework;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Services;

/// <summary>
/// Contract tests for IMfaResetService (US-32, IDN-06, ADM-01).
/// They run against the fake now. When the real service exists, point CreateService() at it.
/// </summary>
[TestFixture]
[Category("IDN-06")]
public class MfaResetContractTests
{
    private FakeMfaResetService _service = null!;
    private List<Guid> _revokedUsers = null!;
    private Guid _admin, _otherAdmin, _student, _studentNoMfa;

    [SetUp]
    public void SetUp()
    {
        _revokedUsers = new List<Guid>();
        _service = new FakeMfaResetService((userId, _) =>
        {
            _revokedUsers.Add(userId);
            return Task.FromResult(3); // pretend the user had 3 sessions
        });

        _admin = Guid.NewGuid();
        _otherAdmin = Guid.NewGuid();
        _student = Guid.NewGuid();
        _studentNoMfa = Guid.NewGuid();

        _service.AddUser(_admin, isAdministrator: true, mfaEnabled: true);
        _service.AddUser(_otherAdmin, isAdministrator: true, mfaEnabled: true);
        _service.AddUser(_student, isAdministrator: false, mfaEnabled: true);
        _service.AddUser(_studentNoMfa, isAdministrator: false, mfaEnabled: false);
    }

    private IMfaResetService Service => _service;

    [Test]
    public async Task Admin_ResetsOtherAccount_MfaRemoved_AllSessionsRevoked()
    {
        var result = await Service.ResetAsync(_admin, _student, "lost_device");

        Assert.That(result.Outcome, Is.EqualTo(MfaResetOutcome.Success));
        Assert.That(result.MfaWasEnabled, Is.True);
        Assert.That(result.SessionsRevoked, Is.EqualTo(3));
        Assert.That(_service.IsMfaEnabled(_student), Is.False);
        Assert.That(_revokedUsers, Is.EqualTo(new[] { _student }));
    }

    [Test]
    public async Task Admin_ResetsAccountWithoutMfa_IsSuccess_NotAnError()
    {
        var result = await Service.ResetAsync(_admin, _studentNoMfa, "other");

        Assert.That(result.Outcome, Is.EqualTo(MfaResetOutcome.Success));
        Assert.That(result.MfaWasEnabled, Is.False);
    }

    [Test]
    public async Task Admin_CanResetAnotherAdmin()
    {
        var result = await Service.ResetAsync(_admin, _otherAdmin, "lost_device");

        Assert.That(result.Outcome, Is.EqualTo(MfaResetOutcome.Success));
        Assert.That(_service.IsMfaEnabled(_otherAdmin), Is.False);
    }

    [Test]
    public async Task NonAdmin_IsDenied_StateUnchanged_NoSessionsRevoked()
    {
        var result = await Service.ResetAsync(_student, _otherAdmin, "other");

        Assert.That(result.Outcome, Is.EqualTo(MfaResetOutcome.NotAuthorized));
        Assert.That(_service.IsMfaEnabled(_otherAdmin), Is.True);
        Assert.That(_revokedUsers, Is.Empty);
    }

    [Test]
    public async Task NonAdmin_CannotLearnWhetherTargetExists()
    {
        var existing = await Service.ResetAsync(_student, _otherAdmin, "other");
        var unknown = await Service.ResetAsync(_student, Guid.NewGuid(), "other");

        Assert.That(unknown.Outcome, Is.EqualTo(existing.Outcome));
        Assert.That(unknown.Outcome, Is.EqualTo(MfaResetOutcome.NotAuthorized));
    }

    [Test]
    public async Task Admin_CannotResetOwnMfa()
    {
        var result = await Service.ResetAsync(_admin, _admin, "other");

        Assert.That(result.Outcome, Is.EqualTo(MfaResetOutcome.SelfResetNotAllowed));
        Assert.That(_service.IsMfaEnabled(_admin), Is.True);
        Assert.That(_revokedUsers, Is.Empty);
    }

    [Test]
    public async Task UnknownTarget_ReturnsNotFound()
    {
        var result = await Service.ResetAsync(_admin, Guid.NewGuid(), "lost_device");

        Assert.That(result.Outcome, Is.EqualTo(MfaResetOutcome.TargetNotFound));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("line one\nline two")]
    [TestCase("lost phone")]
    [TestCase("LOST_DEVICE")]
    [TestCase("not_in_the_list")]
    public async Task InvalidReason_IsRejected_StateUnchanged(string reason)
    {
        var result = await Service.ResetAsync(_admin, _student, reason);

        Assert.That(result.Outcome, Is.EqualTo(MfaResetOutcome.InvalidReason));
        Assert.That(_service.IsMfaEnabled(_student), Is.True);
        Assert.That(_revokedUsers, Is.Empty);
    }

    [Test]
    public void EmptyGuids_AreRejected()
    {
        Assert.ThrowsAsync<ArgumentException>(() => Service.ResetAsync(Guid.Empty, _student, "other"));
        Assert.ThrowsAsync<ArgumentException>(() => Service.ResetAsync(_admin, Guid.Empty, "other"));
    }
}
