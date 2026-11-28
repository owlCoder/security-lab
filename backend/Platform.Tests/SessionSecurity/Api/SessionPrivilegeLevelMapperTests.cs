using Platform.Api.Modules.SessionSecurity.Sessions;
using Platform.Identity.Enums;
using Platform.SessionSecurity.Domain;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.Api;

[TestFixture]
public sealed class SessionPrivilegeLevelMapperTests
{
    // Not defined in either enum.
    private const int UndefinedLevel = 42;

    [Test]
    public void ToIdentity_WhenLevelIsPreAuthentication_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SessionPrivilegeLevelMapper.ToIdentity(SessionPrivilegeLevel.PreAuthentication));
    }

    [Test]
    public void ToIdentity_WhenLevelIsUndefined_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SessionPrivilegeLevelMapper.ToIdentity((SessionPrivilegeLevel)UndefinedLevel));
    }

    [Test]
    public void ToCore_WhenLevelIsUndefined_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SessionPrivilegeLevelMapper.ToCore((AuthLevel)UndefinedLevel));
    }

    [Test]
    public void ToCore_WhenMfaPending_ReturnsPreAuthentication()
    {
        Assert.That(
            SessionPrivilegeLevelMapper.ToCore(AuthLevel.MfaPending),
            Is.EqualTo(SessionPrivilegeLevel.PreAuthentication));
    }

    [Test]
    public void ToCore_WhenActive_ReturnsAuthenticated()
    {
        Assert.That(
            SessionPrivilegeLevelMapper.ToCore(AuthLevel.Active),
            Is.EqualTo(SessionPrivilegeLevel.Authenticated));
    }

    [Test]
    public void ToIdentity_WhenAuthenticated_ReturnsActive()
    {
        Assert.That(
            SessionPrivilegeLevelMapper.ToIdentity(SessionPrivilegeLevel.Authenticated),
            Is.EqualTo(AuthLevel.Active));
    }

    // D3: Identity has no level above Active, so Elevated is stored as Active (documented limitation).
    [Test]
    public void ToIdentity_WhenElevated_ReturnsActive()
    {
        Assert.That(
            SessionPrivilegeLevelMapper.ToIdentity(SessionPrivilegeLevel.Elevated),
            Is.EqualTo(AuthLevel.Active));
    }
}
