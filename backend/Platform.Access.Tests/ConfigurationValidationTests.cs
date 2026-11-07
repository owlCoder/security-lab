using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.Access.Application.Options;
using Platform.Access.Application.Services;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public class ConfigurationValidationTests
{
    

    [Test]
    public void ACC05_Catalog_ContainsExactlyAccessActions_AllValidAndUnique()
    {
        var catalog = new InMemoryActionCatalog();
        var ids = AccessActions.All.Select(a => a.Id).ToArray();

        Assert.That(ids.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(ids.Length));
        Assert.That(catalog.Definitions.Count, Is.EqualTo(ids.Length));
        Assert.That(AccessActions.RequiredIds.All(id => catalog.Contains(id)), Is.True);
        Assert.That(ids.All(id => catalog.Contains(id)), Is.True);
    }

    [Test]
    public void ACC05_Catalog_RejectsDuplicateRegistration()
    {
        var catalog = new InMemoryActionCatalog();

        Assert.That(() => catalog.Register(AccessActions.LabRead),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [TestCase("Lab.Create")]
    [TestCase("lab.create ")]
    [TestCase("lab.create\n")]
    [TestCase("lab")]
    [TestCase("")]
    public void ACC05_Catalog_RejectsInvalidActionId(string action)
    {
        var catalog = new InMemoryActionCatalog();

        Assert.That(() => catalog.Register(new ActionDefinition(action, ActionScope.System, null, false)),
            Throws.InstanceOf<ActionCatalogValidationException>());
        Assert.That(catalog.Contains(action), Is.False);
    }

    [Test]
    public void ACC05_Catalog_RejectsCourseActionWithoutResourceType()
    {
        var catalog = new InMemoryActionCatalog();

        Assert.That(() => catalog.Register(new ActionDefinition("lab.export", ActionScope.Course, null, false)),
            Throws.InstanceOf<ActionCatalogValidationException>());
    }

    
    [Test]
    public void ACC05_Registry_RejectsDuplicatePolicy()
    {
        var registry = new InMemoryAccessPolicyRegistry();
        registry.Register("lab.read", FakePolicy.Permit());

        Assert.That(() => registry.Register("lab.read", FakePolicy.Refuse()),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [TestCase("Lab.Read")]
    [TestCase("lab.read\n")]
    [TestCase("lab")]
    public void ACC05_Registry_RejectsInvalidActionId(string action)
    {
        var registry = new InMemoryAccessPolicyRegistry();

        Assert.That(() => registry.Register(action, FakePolicy.Permit()),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC05_Registry_RejectsNullAndBlankIdentityPolicies()
    {
        var registry = new InMemoryAccessPolicyRegistry();

        Assert.That(() => registry.Register("lab.read", null!), Throws.InstanceOf<ArgumentNullException>());
        Assert.That(() => registry.Register("lab.read", new FakePolicy((_, _) => ValueTask.FromResult(true), id: " ")),
            Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => registry.Register("lab.read", new FakePolicy((_, _) => ValueTask.FromResult(true), version: "")),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    

    [Test]
    public void ACC05_Validator_PolicyForUnregisteredAction_Rejected()
    {
        var catalog = new InMemoryActionCatalog();

        Assert.That(
            () => AccessConfigurationValidator.Validate(catalog, ["attempt.export"], new AccessConfigurationOptions()),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC05_Validator_InvalidActionIdInRegistry_Rejected()
    {
        var catalog = new InMemoryActionCatalog();

        Assert.That(
            () => AccessConfigurationValidator.Validate(catalog, ["Lab.Read"], new AccessConfigurationOptions()),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC05_Validator_ExemptionForUnknownAction_Rejected()
    {
        var catalog = new InMemoryActionCatalog();
        var options = new AccessConfigurationOptions { ActionsWithoutPolicy = new HashSet<string> { "nope.nothing" } };

        Assert.That(() => AccessConfigurationValidator.Validate(catalog, [], options),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC05_Validator_ExemptActionThatAlsoHasPolicy_Rejected()
    {
        var catalog = new InMemoryActionCatalog();
        var options = new AccessConfigurationOptions { ActionsWithoutPolicy = new HashSet<string> { "lab.read" } };

        Assert.That(() => AccessConfigurationValidator.Validate(catalog, ["lab.read"], options),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC05_Validator_LenientMode_ReportsUncoveredActionsWithoutThrowing()
    {
        var catalog = new InMemoryActionCatalog();

        var uncovered = AccessConfigurationValidator.Validate(catalog, ["lab.read"], new AccessConfigurationOptions());

        Assert.That(uncovered.Contains("lab.read"), Is.False);
        Assert.That(uncovered.Count, Is.EqualTo(AccessActions.All.Count - 1));
    }

    [Test]
    public void ACC05_Validator_StrictMode_RejectsActionWithoutPolicyOrExemption()
    {
        var catalog = new InMemoryActionCatalog();
        var options = new AccessConfigurationOptions { RequirePolicyCoverage = true };

        Assert.That(() => AccessConfigurationValidator.Validate(catalog, ["lab.read"], options),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC05_Validator_StrictMode_AcceptsFullCoverageViaPoliciesAndExemptions()
    {
        var catalog = new InMemoryActionCatalog();
        var all = AccessActions.All.Select(a => a.Id).ToArray();
        var options = new AccessConfigurationOptions
        {
            RequirePolicyCoverage = true,
            ActionsWithoutPolicy = all.Skip(1).ToHashSet(StringComparer.Ordinal)
        };

        var uncovered = AccessConfigurationValidator.Validate(catalog, [all[0]], options);

        Assert.That(uncovered.Count, Is.EqualTo(0));
    }

    
    [Test]
    public async Task ACC05_Startup_DefaultConfiguration_Succeeds()
    {
        await using var provider = BuildProvider(_ => { });

        await StartCheckAsync(provider);
    }

    [Test]
    public async Task ACC05_Startup_PolicyForUnknownAction_PreventsStart()
    {
        await using var provider = BuildProvider(s => { });
        provider.GetRequiredService<InMemoryAccessPolicyRegistry>().Register("attempt.export", FakePolicy.Permit());

        Assert.ThrowsAsync<AccessConfigurationException>(async () => await StartCheckAsync(provider));
    }

    [Test]
    public async Task ACC05_Startup_StrictCoverageWithMissingPolicies_PreventsStart()
    {
        await using var provider = BuildProvider(s =>
            s.AddSingleton(new AccessConfigurationOptions { RequirePolicyCoverage = true }));
       
        provider.GetRequiredService<InMemoryActionCatalog>().Register("attempt.export");

        Assert.ThrowsAsync<AccessConfigurationException>(async () => await StartCheckAsync(provider));
    }

    

    [Test]
    public async Task ACC05_Composition_ResolvedEngine_DeniesUnknownAction()
    {
        await using var provider = BuildProvider(_ => { });
        var decision = await provider.GetRequiredService<IAccessDecision>().DecideAsync(
            new AccessRequest(new SubjectRef("demo", AccountState.Active), "demo.unknown", null, "c1"));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.UnknownAction));
    }

    [Test]
    public async Task ACC05_Composition_NewKnownActionWithoutPolicy_DeniedByDefault()
    {
        
        await using var provider = BuildProvider(_ => { });
        provider.GetRequiredService<InMemoryActionCatalog>().Register("attempt.export");

        var decision = await provider.GetRequiredService<IAccessDecision>().DecideAsync(
            new AccessRequest(new SubjectRef("demo", AccountState.Active), "attempt.export", null, "c2"));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.NoPolicy));
    }

    [Test]
    public async Task ACC05_Composition_CourseActionWithoutMembershipData_NeverAllowed()
    {
       
        await using var provider = BuildProvider(_ => { });

        var decision = await provider.GetRequiredService<IAccessDecision>().DecideAsync(
            new AccessRequest(new SubjectRef("demo", AccountState.Active), "lab.read", new ResourceRef("laboratory", "1"), "c3"));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services); 
        services.AddAccessControl();
        return services.BuildServiceProvider();
    }

    private static async Task StartCheckAsync(IServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
        }
    }
}
