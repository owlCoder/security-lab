using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Audit.Application;
using Platform.Audit.Application.CourseView;
using Platform.Audit.Contracts;
using Platform.Audit.Endpoints;
using Platform.Audit.Infrastructure.Persistence;

namespace Platform.Audit.Composition;

public static class CourseAuditViewServiceCollectionExtensions
{
    /// <summary>
    /// Course audit endpoint (AUD-06, S-14) with the S-07 query service. Call after
    /// <c>AddAccessControl()</c>, <c>AddAccessAspNet()</c>, <c>AddAudit()</c> and
    /// <c>AddAuditPersistence(...)</c>; with ValidateOnBuild a missing prerequisite is a startup error.
    /// The query service is registered only here (agreed with S-07): it does no authorization itself,
    /// so it must not be resolvable in a host without the protected endpoint around it.
    /// </summary>
    public static IServiceCollection AddCourseAuditView(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Singleton like IAccessDecision: no request state is kept.
        services.TryAddSingleton<CourseAuditReadAuthorizer>();

        // Scoped like ITrustedAuditWriter (AuditDbContext); never captured by a singleton.
        services.TryAddScoped<CourseAuditViewRecorder>();

        // S-07: the normalizer only needs the clock; the reader and the service use AuditDbContext.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<CourseAuditQueryNormalizer>();
        services.TryAddScoped<ICourseAuditEventReader, PostgresCourseAuditEventReader>();
        services.TryAddScoped<ICourseAuditQueryService, CourseAuditQueryService>();

        return services;
    }

    /// <summary>
    /// Maps <c>GET /api/courses/{courseId:guid}/audit-events</c> with its Access requirement
    /// (<c>audit.events.read</c> on <c>course/{courseId}</c>). Needs <see cref="AddCourseAuditView"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapCourseAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var registered = endpoints.ServiceProvider.GetService<IServiceProviderIsService>();
        if (registered is null || !registered.IsService(typeof(CourseAuditReadAuthorizer)))
        {
            throw new InvalidOperationException(
                "Call AddCourseAuditView() before MapCourseAuditEndpoints(); the course audit endpoint is not mapped without its authorization.");
        }

        endpoints.MapGet(CourseAuditEndpointRoute.Pattern, CourseAuditEndpointHandler.HandleAsync)
            .RequireCourseAuditRead()
            .WithName("GetCourseAuditEvents");

        return endpoints;
    }
}
