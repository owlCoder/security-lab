using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.Api.Modules.Identity.SessionExpiry;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("US-14")]
public class SessionOptionsExtensionsTests
{
    [Test]
    public void AddSessionExpiry_BindsConfiguredValues()
    {
        using var provider = CreateProvider("600", "90", "10");

        var options = provider.GetRequiredService<IOptions<SessionOptions>>().Value;

        Assert.That(options.AbsoluteLifetimeMinutes, Is.EqualTo(600));
        Assert.That(options.ActivityTimeoutMinutes, Is.EqualTo(90));
        Assert.That(options.MfaPendingTimeoutMinutes, Is.EqualTo(10));
    }

    [TestCase("0", "45", "5", "All session timeout values must be positive.")]
    [TestCase("480", "45", "45", "MFA pending timeout must be less than the activity timeout.")]
    [TestCase("480", "481", "5", "Activity timeout must not exceed the absolute session lifetime.")]
    public void AddSessionExpiry_InvalidConfiguration_ReportsTheRelevantValidationError(
        string absoluteLifetime,
        string activityTimeout,
        string mfaPendingTimeout,
        string expectedFailure)
    {
        using var provider = CreateProvider(absoluteLifetime, activityTimeout, mfaPendingTimeout);

        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<SessionOptions>>().Value;
        });

        Assert.That(exception!.Failures, Does.Contain(expectedFailure));
    }

    private static ServiceProvider CreateProvider(
        string absoluteLifetime,
        string activityTimeout,
        string mfaPendingTimeout)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SessionOptions.Section}:{nameof(SessionOptions.AbsoluteLifetimeMinutes)}"] = absoluteLifetime,
                [$"{SessionOptions.Section}:{nameof(SessionOptions.ActivityTimeoutMinutes)}"] = activityTimeout,
                [$"{SessionOptions.Section}:{nameof(SessionOptions.MfaPendingTimeoutMinutes)}"] = mfaPendingTimeout
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSessionExpiry(configuration);
        return services.BuildServiceProvider();
    }
}
