using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Infrastructure.Auditing;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;

namespace Platform.Access.Composition;

public static class AccessServiceCollectionExtensions
{
    public static IServiceCollection AddAccessControl(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryActionCatalog>();
        services.AddSingleton<IActionCatalog>(sp => sp.GetRequiredService<InMemoryActionCatalog>());
        services.AddSingleton<InMemoryAccessPolicyRegistry>();
        services.AddSingleton<IAccessPolicyRegistry>(sp => sp.GetRequiredService<InMemoryAccessPolicyRegistry>());
        services.AddSingleton<IAccessAuditWriter, NullAccessAuditWriter>();
        services.AddSingleton<IAccessDecision, DefaultDenyAccessDecision>();

        return services;
    }
}
