using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Platform.Identity.Tests;

// US-18: MFA lockout settings, separate from the IDN-05 password lockout. No database needed.
[TestFixture]
[Category("US-18")]
public sealed class MfaLockoutOptionsTests
{
    [Test]
    public void AddIdentityModule_WithoutSettings_LocksAfterFiveCodesForFifteenMinutes()
    {
        // Arrange
        using var provider = Build(new Dictionary<string, string?>());

        // Act
        var lockout = provider.GetRequiredService<IOptions<MfaLockoutOptions>>().Value;

        // Assert
        Assert.That(lockout.MaxFailedAttempts, Is.EqualTo(5));
        Assert.That(lockout.LockoutMinutes, Is.EqualTo(15));
    }

    [Test]
    public void AddIdentityModule_WithMfaSettings_UsesThem()
    {
        // Arrange
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Mfa:Lockout:MaxFailedAttempts"] = "3",
            ["Identity:Mfa:Lockout:LockoutMinutes"] = "60"
        });

        // Act
        var lockout = provider.GetRequiredService<IOptions<MfaLockoutOptions>>().Value;

        // Assert
        Assert.That(lockout.MaxFailedAttempts, Is.EqualTo(3));
        Assert.That(lockout.LockoutMinutes, Is.EqualTo(60));
    }

    [Test]
    public void AddIdentityModule_WithPasswordLockoutSettings_DoesNotChangeTheMfaLockout()
    {
        // Arrange
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Lockout:MaxFailedAccessAttempts"] = "20",
            ["Identity:Lockout:LockoutMinutes"] = "1"
        });

        // Act
        var lockout = provider.GetRequiredService<IOptions<MfaLockoutOptions>>().Value;

        // Assert
        Assert.That(lockout.MaxFailedAttempts, Is.EqualTo(5));
        Assert.That(lockout.LockoutMinutes, Is.EqualTo(15));
    }

    // 20 codes a minute is allowed for the password lockout, but would give an attacker about 8% a day.
    [TestCase("0", "15")]
    [TestCase("11", "15")]
    [TestCase("20", "1")]
    [TestCase("5", "14")]
    [TestCase("5", "1441")]
    [TestCase("-1", "-1")]
    public void AddIdentityModule_WithMfaSettingsOutOfRange_FailsStartupValidation(string attempts, string minutes)
    {
        // Arrange
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Mfa:Lockout:MaxFailedAttempts"] = attempts,
            ["Identity:Mfa:Lockout:LockoutMinutes"] = minutes
        });

        // Act
        var validate = () => provider.GetRequiredService<IStartupValidator>().Validate();

        // Assert
        Assert.That(validate, Throws.TypeOf<OptionsValidationException>()
            .With.Message.Contains(MfaLockoutOptions.Section));
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityModule(configuration);
        return services.BuildServiceProvider();
    }
}
