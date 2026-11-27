using Microsoft.Extensions.Configuration;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("US-19")]
public class RoleMfaPolicyTests
{
    private RoleMfaPolicy _policy = null!;

    [SetUp]
    public void SetUp()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Mfa:RequiredForRoles:0"] = "Administrator",
                ["Identity:Mfa:RequiredForRoles:1"] = "Teacher"
            })
            .Build();

        _policy = new RoleMfaPolicy(configuration);
    }
        
    [Test]
    public void IsRequiredFor_AdministratorInList_ReturnsTrue()
    {
        var result = _policy.IsRequiredFor(AccountRole.Administrator);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsRequiredFor_StudentNotInList_ReturnsFalse()
    {
        var result = _policy.IsRequiredFor(AccountRole.Student);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsRequiredFor_TeacherInList_ReturnsTrue()
    {
        var result = _policy.IsRequiredFor(AccountRole.Teacher);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsRequiredFor_EmptyList_ReturnsFalse()
    {
        var policy = new RoleMfaPolicy(new ConfigurationBuilder().Build());

        Assert.That(policy.IsRequiredFor(AccountRole.Administrator), Is.False);
    }

    [Test]
    public void IsRequiredFor_RoleWrittenInOtherCase_ReturnsFalse()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Mfa:RequiredForRoles:0"] = "teacher"
            })
            .Build();

        var policy = new RoleMfaPolicy(configuration);

        Assert.That(policy.IsRequiredFor(AccountRole.Teacher), Is.False);
    }
}