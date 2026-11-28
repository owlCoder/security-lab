using Microsoft.Extensions.Options;
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
        _policy = PolicyFor("Administrator", "Teacher");
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
        var policy = PolicyFor();

        Assert.That(policy.IsRequiredFor(AccountRole.Administrator), Is.False);
    }

    [Test]
    public void Validator_KnownRoles_Succeeds()
    {
        var result = Validate("Administrator", "Teacher");

        Assert.That(result.Succeeded, Is.True);
    }

    [TestCase("teacher")]
    [TestCase("Teachr")]
    public void Validator_UnknownOrMiscasedRole_Fails(string role)
    {
        var result = Validate(role);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain(role));
    }

    private static RoleMfaPolicy PolicyFor(params string[] roles) =>
        new(Options.Create(new MfaPolicyOptions { RequiredForRoles = [.. roles] }));

    private static ValidateOptionsResult Validate(params string[] roles) =>
        new MfaPolicyOptionsValidator().Validate(null, new MfaPolicyOptions { RequiredForRoles = [.. roles] });
}
