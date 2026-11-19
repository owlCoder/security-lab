using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Audit.Composition;

namespace Platform.Audit.Tests;

// Module registrations must survive strict DI validation (composition level).
[TestFixture]
public sealed class CompositionScopeTests
{
    private static readonly ServiceProviderOptions StrictOptions = new()
    {
        ValidateScopes = true,
        ValidateOnBuild = true,
    };

    [Test]
    public void AccessAndAuditRegistrations_PassScopeAndBuildValidation()
    {
        var services = new ServiceCollection();
        services.AddAudit();
        services.AddAccessControl();

        using var provider = services.BuildServiceProvider(StrictOptions);
        using var scope = provider.CreateScope();

        var decision = scope.ServiceProvider.GetRequiredService<IAccessDecision>();

        Assert.That(decision, Is.Not.Null);
    }

    [Test]
    public void CaptiveDependency_IsRejectedByValidation()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddSingleton<SingletonConsumer>();

        var exception = Assert.Catch<Exception>(() =>
        {
            services.BuildServiceProvider(StrictOptions);
        });

        Assert.That(exception?.ToString(), Does.Contain("scoped").IgnoreCase);
    }
}
