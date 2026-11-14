using Platform.Audit.Application;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Domain.Constants;
using Platform.Audit.Application.Authorization;
using Platform.Audit.Contracts;
using Platform.Audit.Correlation;

namespace Platform.Audit.Composition;

public static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(provider => provider.GetRequiredService<CorrelationContext>());


        // PR #109 declares this central action. Access owns the catalog and singleton
        // lifetime; Audit supplies only its policy through the public composition hook.
        services.AddAccessPolicy<CourseAuditReadPolicy>(AccessActions.AuditEventsRead.Id);

        return services;
    }

public static IServiceCollection AddMfaBackupCodeAudit(
    this IServiceCollection services)
{
    services.AddScoped<
        IMfaBackupCodeAuditService,
        MfaBackupCodeAuditService>();

    return services;
}

}
