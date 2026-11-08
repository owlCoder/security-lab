using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Audit.Application.CourseView;

namespace Platform.Audit.Composition;

public static class CourseAuditViewServiceCollectionExtensions
{
    /// <summary>
    /// S-14 building blocks of the course audit endpoint. Call after <c>AddAccessControl()</c>,
    /// <c>AddAudit()</c> and <c>AddAuditPersistence(...)</c>; with ValidateOnBuild a missing
    /// prerequisite is a startup error. Not called by Platform.Api yet: the endpoint and this
    /// registration are wired together once the S-07 query service is on main.
    /// </summary>
    public static IServiceCollection AddCourseAuditView(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Singleton like IAccessDecision: no request state is kept.
        services.TryAddSingleton<CourseAuditReadAuthorizer>();

        // Scoped like ITrustedAuditWriter (AuditDbContext); never captured by a singleton.
        services.TryAddScoped<CourseAuditViewRecorder>();

        return services;
    }
}
