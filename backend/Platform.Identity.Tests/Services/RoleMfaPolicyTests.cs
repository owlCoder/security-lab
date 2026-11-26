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
}