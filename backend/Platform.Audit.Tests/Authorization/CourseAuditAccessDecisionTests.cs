using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Infrastructure.Membership;
using Platform.Audit.Application.Authorization;
using Platform.Audit.Composition;
using static Platform.Audit.Tests.Authorization.AuditAuthorizationExamples;

namespace Platform.Audit.Tests.Authorization;

[TestFixture]
public sealed class CourseAuditAccessDecisionTests
{
    private static ServiceProviderOptions StrictOptions => new()
    {
        ValidateScopes = true,
        ValidateOnBuild = true
    };

    [Test]
    public void AddAudit_BindsExactlyOnePolicyToTheExistingCentralAction()
    {
        var services = new ServiceCollection();
        services.AddAccessControl();
        services.AddAudit();
        using var provider = services.BuildServiceProvider(StrictOptions);
        var catalog = provider.GetRequiredService<IActionCatalog>();
        var registry = provider.GetRequiredService<IAccessPolicyRegistry>();
        var found = registry.TryGet(CourseAuditReadPolicy.ActionId, out var registered);
        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(registered, Is.TypeOf<CourseAuditReadPolicy>());
            Assert.That(catalog.Definitions.Count(action => action.Id == CourseAuditReadPolicy.ActionId), Is.EqualTo(1));
            Assert.That(catalog.TryGet(CourseAuditReadPolicy.ActionId, out var definition), Is.True);
            Assert.That(definition, Is.EqualTo(AccessActions.AuditEventsRead));
            Assert.That(definition!.Scope, Is.EqualTo(ActionScope.Course));
            Assert.That(definition.ResourceType, Is.EqualTo("course"));
            Assert.That(services.Count(service => service.ServiceType == typeof(CourseAuditReadPolicy)), Is.EqualTo(1));
            Assert.That(services.Single(service => service.ServiceType == typeof(CourseAuditReadPolicy)).Lifetime,
                Is.EqualTo(ServiceLifetime.Singleton));
        });
    }

    [Test]
    public async Task SingletonPolicy_ResolvesFromDifferentScopes_WithoutCapturingRequestState()
    {
        var memberships = new StubCourseAuditMemberships();
        memberships.Set(Membership());
        var services = new ServiceCollection();
        services.AddSingleton<IMembershipReader>(memberships);
        services.AddAccessControl();
        services.AddAudit();
        using var provider = services.BuildServiceProvider(StrictOptions);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var firstPolicy = firstScope.ServiceProvider.GetRequiredService<CourseAuditReadPolicy>();
        var secondPolicy = secondScope.ServiceProvider.GetRequiredService<CourseAuditReadPolicy>();
        Assert.That(firstPolicy, Is.SameAs(secondPolicy));
        Assert.That(await firstPolicy.EvaluateAsync(Request()), Is.True);
        Assert.That(await secondPolicy.EvaluateAsync(Request(accountId: OtherAccountId)), Is.False);
        memberships.Set(Membership() with { Status = MembershipStatus.Left });
        Assert.That(await secondPolicy.EvaluateAsync(Request()), Is.False);
    }

    [Test]
    public void ScopedMembershipProvider_IsRejectedByStrictValidation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMembershipReader, StubCourseAuditMemberships>();
        services.AddAccessControl();
        services.AddAudit();
        var exception = Assert.Catch<Exception>(() =>
        {
            using var provider = services.BuildServiceProvider(StrictOptions);
            provider.GetRequiredService<IAccessPolicyRegistry>();
        });
        Assert.That(exception!.ToString(), Does.Contain("scoped").IgnoreCase);
    }

    [Test]
    public async Task ProductionDefaultMembershipFallback_KeepsRegisteredAuditActionDenied()
    {
        var services = new ServiceCollection();
        services.AddAccessControl();
        services.AddAudit();
        using var provider = services.BuildServiceProvider(StrictOptions);
        var result = await provider.GetRequiredService<IAccessDecision>().DecideAsync(Request());
        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<IMembershipReader>(), Is.TypeOf<EmptyMembershipReader>());
            Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
            Assert.That(result.DenialReason, Is.EqualTo(AccessDenialReason.PolicyDenied));
        });
    }

    [Test]
    public async Task PublicComposition_UsesRealAccessDecision_AndAuditsEachDecisionOnce()
    {
        var memberships = new StubCourseAuditMemberships();
        memberships.Set(Membership());
        memberships.Set(Membership(CourseRole.Student, courseId: CourseL));
        var audit = new RecordingAccessAuditWriter();
        var services = new ServiceCollection();
        services.AddSingleton<IMembershipReader>(memberships);
        services.AddAccessControl();
        services.AddAudit();
        // This replacement is test-only; it does not change production auditing.
        services.Replace(ServiceDescriptor.Singleton<IAccessAuditWriter>(audit));
        using var provider = services.BuildServiceProvider(StrictOptions);
        var access = provider.GetRequiredService<IAccessDecision>();
        var allowed = await access.DecideAsync(Request(correlationId: "allowed-request"));
        var denied = await access.DecideAsync(Request(courseId: CourseL, correlationId: "denied-request"));
        Assert.Multiple(() =>
        {
            Assert.That(allowed.Outcome, Is.EqualTo(AccessOutcome.Allow));
            Assert.That(allowed.PolicyId, Is.EqualTo("audit.course.read"));
            Assert.That(allowed.PolicyVersion, Is.EqualTo("v1"));
            Assert.That(allowed.CorrelationId, Is.EqualTo("allowed-request"));
            Assert.That(denied.Outcome, Is.EqualTo(AccessOutcome.Deny));
            Assert.That(denied.DenialReason, Is.EqualTo(AccessDenialReason.PolicyDenied));
            Assert.That(denied.CorrelationId, Is.EqualTo("denied-request"));
            Assert.That(audit.Entries, Has.Count.EqualTo(2));
            Assert.That(audit.Entries.Select(entry => entry.Decision), Is.EqualTo(new[] { allowed, denied }));
        });
    }

    [Test]
    public async Task CentralActionWithoutAuditModule_HasNoPolicyAndStaysDenied()
    {
        var services = new ServiceCollection();
        services.AddAccessControl();
        using var provider = services.BuildServiceProvider(StrictOptions);
        var result = await provider.GetRequiredService<IAccessDecision>().DecideAsync(Request());
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
            Assert.That(result.DenialReason, Is.EqualTo(AccessDenialReason.NoPolicy));
        });
    }

    [Test]
    public void DuplicatePolicyBinding_IsRejectedThroughThePublicHook()
    {
        var services = new ServiceCollection();
        services.AddAccessControl();
        services.AddAudit();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddAccessPolicy<CourseAuditReadPolicy>(CourseAuditReadPolicy.ActionId));
    }
}
