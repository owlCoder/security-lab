using Platform.Access.Application.Options;
using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;

namespace Platform.Access.Tests.Support;

public sealed class EngineFixture
{
    public InMemoryActionCatalog Catalog { get; } = new();
    public InMemoryAccessPolicyRegistry Registry { get; } = new();
    public RecordingAuditWriter Audit { get; } = new();

    public EngineFixture WithPolicy(string action, IAccessPolicy policy)
    {
        Registry.Register(action, policy);
        return this;
    }

    public EngineFixture WithPermitAllPolicies()
    {
        foreach (var definition in AccessActions.All)
        {
            Registry.Register(definition.Id, FakePolicy.Permit());
        }

        return this;
    }

    public DefaultDenyAccessDecision Engine(
        TimeSpan? policyTimeout = null,
        TimeSpan? auditTimeout = null,
        IAccessAuditWriter? audit = null) =>
        Build(Catalog, Registry, audit ?? Audit, policyTimeout, auditTimeout);

    public static DefaultDenyAccessDecision Build(
        IActionCatalog catalog,
        IAccessPolicyRegistry registry,
        IAccessAuditWriter audit,
        TimeSpan? policyTimeout = null,
        TimeSpan? auditTimeout = null) =>
        new(
            catalog,
            registry,
            audit,
            new AccessDecisionOptions
            {
                PolicyTimeout = policyTimeout ?? TimeSpan.FromSeconds(2),
                AuditTimeout = auditTimeout ?? TimeSpan.FromSeconds(1)
            });
}
