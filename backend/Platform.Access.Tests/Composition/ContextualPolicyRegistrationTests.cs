
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Application.Rules;
using Platform.Access.Application.Snapshots;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Grants;
using Platform.Access.Infrastructure.Guards;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Policies;

namespace Platform.Access.Tests.Composition;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public sealed class ContextualPolicyRegistrationTests
{
    private sealed class TrueCondition : ICondition<EvaluationSnapshot>
    {
        public Tri Evaluate(EvaluationSnapshot context) => Tri.True;
    }

    [Test]
    public void ACC08_DI_SnapshotLoader_IsRegistered()
    {
        var services = new ServiceCollection();

        services.AddAccessControl();

        using var provider = services.BuildServiceProvider();

        Assert.That(
            provider.GetService<IEvaluationSnapshotLoader>(),
            Is.InstanceOf<EvaluationSnapshotLoader>());
    }

    [Test]
    public void ACC08_DI_GrantReader_HasFailClosedFallback()
    {
        var services = new ServiceCollection();

        services.AddAccessControl();

        using var provider = services.BuildServiceProvider();

        Assert.That(
            provider.GetService<IElevatedGrantReader>(),
            Is.InstanceOf<EmptyElevatedGrantReader>());
    }

    [Test]
    public void ACC08_DI_OwnershipGuard_IsRegistered()
    {
        var services = new ServiceCollection();

        services.AddAccessControl();

        using var provider = services.BuildServiceProvider();

        Assert.That(
            provider.GetService<OwnershipScopeGuard>(),
            Is.Not.Null);
    }

    [Test]
    public void ACC08_AddContextualRules_AfterAddAccessControl_IsRejected()
    {
        var services = new ServiceCollection();

        services.AddAccessControl();

        var rules = new RuleSet<EvaluationSnapshot>(
        [
            new Rule<EvaluationSnapshot>(
                "rule.test",
                new TrueCondition())
        ]);

        Assert.Throws<InvalidOperationException>(() =>
            services.AddContextualRules(
                AccessActions.AttemptRead.Id,
                rules));
    }
}
