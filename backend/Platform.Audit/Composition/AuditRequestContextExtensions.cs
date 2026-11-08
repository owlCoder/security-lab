using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Audit.Application;
using Platform.Audit.Contracts;

namespace Platform.Audit.Composition;

public static class AuditRequestContextExtensions
{
    /// <summary>
    /// Lets the ordinary <see cref="IAuditWriter"/> work inside HTTP requests. Requires
    /// <c>AddAudit()</c> (correlation) and <c>AddAuditPersistence()</c>. The actor source must read
    /// only server-validated identity. Replaces the default "no ambient context" provider
    /// explicitly, so the result does not depend on registration order.
    /// </summary>
    public static IServiceCollection AddAuditRequestContext<TActorSource>(this IServiceCollection services)
        where TActorSource : class, IAuditActorSource
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAuditActorSource, TActorSource>();
        services.Replace(ServiceDescriptor.Scoped<IAuditContextProvider, RequestAuditContextProvider>());
        return services;
    }
}
