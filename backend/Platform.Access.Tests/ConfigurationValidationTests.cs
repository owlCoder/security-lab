using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Platform.Access.AspNet;
using Platform.Access.Application.Options;
using Platform.Access.Application.Services;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Policies;
using Platform.Access.Infrastructure.Resources;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public class ConfigurationValidationTests
{
    private sealed class RegisteredPolicy : IAccessPolicy
    {
        public RegisteredPolicy()
        {
        }

        public string Id => "test.registered";
        public string Version => "1";

        public ValueTask<bool> EvaluateAsync(
            AccessRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class ScopedDependency
    {
    }

    private sealed class ScopedDependencyPolicy : IAccessPolicy
    {
        private readonly ScopedDependency dependency;

        public ScopedDependencyPolicy(ScopedDependency dependency)
        {
            this.dependency = dependency;
        }

        public string Id => "test.scoped-dependency";
        public string Version => "1";

        public ValueTask<bool> EvaluateAsync(
            AccessRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(dependency is not null);
    }

    [Test]
    public void ACC05_Catalog_ContainsExactlyAccessActions_AllValidAndUnique()
    {
        var catalog = new InMemoryActionCatalog();
        var ids = AccessActions.All.Select(a => a.Id).ToArray();

        Assert.That(ids.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(ids.Length));
        Assert.That(catalog.Definitions.Count, Is.EqualTo(ids.Length));
        Assert.That(AccessActions.RequiredIds.All(id => catalog.Contains(id)), Is.True);
        Assert.That(ids.All(id => catalog.Contains(id)), Is.True);
        Assert.That(catalog.TryGet("audit.events.read", out var auditAction), Is.True);
        Assert.That(auditAction, Is.EqualTo(new ActionDefinition(
            "audit.events.read",
            ActionScope.Course,
            "course",
            false)));
    }

    [TestCase("course.read", false)]
    [TestCase("course.create", true)]
    [TestCase("course.archive", true)]
    [TestCase("group.read", false)]
    [TestCase("group.create", true)]
    [TestCase("group.member.add", true)]
    [TestCase("group.member.remove", true)]
    [TestCase("material.read", false)]
    public void ACC05_ApprovedAuthenticatedActions_HaveExpectedScopeAndPrivilege(
        string action,
        bool privileged)
    {
        var catalog = new InMemoryActionCatalog();

        Assert.That(catalog.TryGet(action, out var definition), Is.True);
        Assert.That(definition, Is.EqualTo(new ActionDefinition(
            action,
            ActionScope.Authenticated,
            null,
            privileged)));
    }

    [Test]
    public void ACC05_UnknownAction_RemainsAbsentFromCatalog()
    {
        var catalog = new InMemoryActionCatalog();

        Assert.That(catalog.Contains("course.read.unknown"), Is.False);
        Assert.That(catalog.TryGet("course.read.unknown", out _), Is.False);
    }

    [Test]
    public async Task ACC05_Composition_RegistersAdditionalActionAndPolicyBeforeBuild()
    {
        var services = new ServiceCollection();
        services.AddAccessControl();
        services.AddAccessAction<RegisteredPolicy>(
            new ActionDefinition("example.read", ActionScope.Authenticated, null, false));

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var catalog = provider.GetRequiredService<IActionCatalog>();
        var decision = provider.GetRequiredService<IAccessDecision>();

        Assert.That(catalog.Contains("example.read"), Is.True);
        var result = await decision.DecideAsync(
            new AccessRequest(
                new SubjectRef("user-1", AccountState.Active),
                "example.read",
                null,
                "registration-test"));
        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public void ACC05_Composition_RejectsDuplicateActionAndPolicyRegistrations()
    {
        var services = new ServiceCollection();

        Assert.That(
            () => services.AddAccessAction<RegisteredPolicy>(AccessActions.LabRead),
            Throws.InstanceOf<InvalidOperationException>());
        Assert.That(
            () => services.AddAccessPolicy<RegisteredPolicy>("unknown.action"),
            Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(
            () => services.AddAccessPolicy<RegisteredPolicy>("audit.events.read")
                .AddAccessPolicy<RegisteredPolicy>("audit.events.read"),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public void ACC05_Composition_AcceptsSingletonPolicyRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RegisteredPolicy>();

        Assert.DoesNotThrow(
            () => services.AddAccessPolicy<RegisteredPolicy>("audit.events.read"));
        Assert.That(
            services.Count(descriptor => descriptor.ServiceType == typeof(RegisteredPolicy)),
            Is.EqualTo(1));
    }

    [Test]
    public void ACC05_Composition_ScopeValidationRejectsScopedPolicyDependency()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddSingleton<ScopedDependencyPolicy>();
        services.AddAccessControl();
        services.AddAccessPolicy<ScopedDependencyPolicy>("audit.events.read");

        var exception = Assert.Throws<AggregateException>(
            () => services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));

        Assert.That(
            exception!.Flatten().InnerExceptions,
            Has.Some.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task ACC05_Composition_CoursePolicyUsesSingletonSafeReaders()
    {
        var accountId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var resource = new ResourceRef("laboratory", "lab-1");
        var memberships = new InMemoryMembershipReader();
        memberships.Set(new CourseMembership(
            accountId,
            courseId,
            CourseRole.Teacher,
            MembershipStatus.Active,
            new HashSet<string>(StringComparer.Ordinal)));
        var resources = new InMemoryResourceDescriptorResolver();
        resources.SetCourse(resource, courseId);

        var services = new ServiceCollection();
        services.AddSingleton<IMembershipReader>(memberships);
        services.AddSingleton<IResourceDescriptorResolver>(resources);
        services.AddAccessControl();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var result = await provider.GetRequiredService<IAccessDecision>().DecideAsync(
            new AccessRequest(
                new SubjectRef(accountId.ToString("D"), AccountState.Active),
                "lab.draft.manage",
                resource,
                "scoped-course-policy"));

        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Allow));
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
    public void ACC05_Catalog_RejectsUnknownScope()
    {
        var catalog = new InMemoryActionCatalog();

        Assert.That(
            () => catalog.Register(new ActionDefinition("lab.unknown.scope", ActionScope.Unknown, null, false)),
            Throws.InstanceOf<ActionCatalogValidationException>());
    }

    [Test]
    public void ACC05_Catalog_RejectsInvalidResourceTypeUsingMetadataRules()
    {
        var catalog = new InMemoryActionCatalog();

        Assert.That(
            () => catalog.Register(new ActionDefinition(
                "lab.invalid.resource",
                ActionScope.Course,
                new string('a', 65),
                false)),
            Throws.InstanceOf<ActionCatalogValidationException>());

        var app = WebApplication.CreateBuilder().Build();
        Assert.Throws<ArgumentException>(
            () => app.MapGet("/a", () => "x").RequireAccess("lab.read", new string('a', 65), "id"));
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
    public async Task ACC05_Startup_WithExplicitPolicyExemptions_Succeeds()
    {
        await using var provider = BuildProvider(ConfigureCoursePolicyCoverage);

        await StartCheckAsync(provider);
    }

    [Test]
    public async Task ACC05_Startup_FreezesActionAndPolicyRegistration()
    {
        await using var provider = BuildProvider(ConfigureCoursePolicyCoverage);
        await StartCheckAsync(provider);

        var catalog = provider.GetRequiredService<InMemoryActionCatalog>();
        var registry = provider.GetRequiredService<InMemoryAccessPolicyRegistry>();

        Assert.That(
            () => catalog.Register(new ActionDefinition("module.extra", ActionScope.Authenticated, null, false)),
            Throws.InstanceOf<InvalidOperationException>());
        Assert.That(
            () => registry.Register("module.extra", FakePolicy.Permit()),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task ACC05_Startup_PolicyForUnknownAction_PreventsStart()
    {
        var services = new ServiceCollection();
        Assert.That(
            () => services.AddAccessPolicy<RegisteredPolicy>("attempt.export"),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public async Task ACC05_Startup_StrictCoverageWithMissingPolicies_PreventsStart()
    {
        await using var provider = BuildProvider(s =>
            s.AddSingleton(new AccessConfigurationOptions { RequirePolicyCoverage = true }));

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
        var engine = new EngineFixture();
        engine.Catalog.Register("attempt.export");
        var decision = await engine.Engine().DecideAsync(
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

    [Test]
    public async Task ACC05_Composition_AuditActionWithoutPolicyOrCorrectResource_IsDenied()
    {
        await using var provider = BuildProvider(_ => { });
        var decision = provider.GetRequiredService<IAccessDecision>();

        var missingPolicy = await decision.DecideAsync(
            new AccessRequest(
                new SubjectRef("demo", AccountState.Active),
                "audit.events.read",
                new ResourceRef("course", "course-1"),
                "audit-no-policy"));
        var wrongResource = await decision.DecideAsync(
            new AccessRequest(
                new SubjectRef("demo", AccountState.Active),
                "audit.events.read",
                new ResourceRef("attempt", "attempt-1"),
                "audit-wrong-resource"));

        Assert.That(missingPolicy.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(missingPolicy.DenialReason, Is.EqualTo(AccessDenialReason.NoPolicy));
        Assert.That(wrongResource.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(wrongResource.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public void ACC05_Composition_AddAccessControl_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddAccessControl();
        services.AddAccessControl();

        Assert.That(services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)), Is.EqualTo(1));
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services); 
        services.AddAccessControl();
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static void ConfigureCoursePolicyCoverage(IServiceCollection services)
    {
        var actionsWithPolicies = CourseRolePolicies.All
            .Select(definition => definition.Action.Id)
            .ToHashSet(StringComparer.Ordinal);
        var actionsWithoutPolicies = AccessActions.All
            .Select(definition => definition.Id)
            .Where(action => !actionsWithPolicies.Contains(action))
            .ToHashSet(StringComparer.Ordinal);

        services.AddSingleton(new AccessConfigurationOptions
        {
            RequirePolicyCoverage = true,
            ActionsWithoutPolicy = actionsWithoutPolicies
        });
    }

    private static async Task StartCheckAsync(IServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
        }
    }
}
