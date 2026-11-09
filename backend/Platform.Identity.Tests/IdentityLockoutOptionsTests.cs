using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Platform.Identity.Tests;

// IDN-05: lockout settings. No database needed.
[TestFixture]
[Category("IDN-05")]
public sealed class IdentityLockoutOptionsTests
{
    [Test]
    public void Defaults_lock_after_five_failures_for_fifteen_minutes()
    {
        using var provider = Build(new Dictionary<string, string?>());

        var lockout = provider.GetRequiredService<IOptions<IdentityLockoutOptions>>().Value;

        Assert.That(lockout.MaxFailedAccessAttempts, Is.EqualTo(5));
        Assert.That(lockout.LockoutMinutes, Is.EqualTo(15));
    }

    [Test]
    public void Configured_values_are_used()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Lockout:MaxFailedAccessAttempts"] = "3",
            ["Identity:Lockout:LockoutMinutes"] = "30"
        });

        var lockout = provider.GetRequiredService<IOptions<IdentityLockoutOptions>>().Value;

        Assert.That(lockout.MaxFailedAccessAttempts, Is.EqualTo(3));
        Assert.That(lockout.LockoutMinutes, Is.EqualTo(30));
    }

    [TestCase("0", "15")]
    [TestCase("21", "15")]
    [TestCase("5", "0")]
    [TestCase("5", "1441")]
    [TestCase("-1", "-1")]
    public void Out_of_range_values_fail_startup_validation(string attempts, string minutes)
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Lockout:MaxFailedAccessAttempts"] = attempts,
            ["Identity:Lockout:LockoutMinutes"] = minutes
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());
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
