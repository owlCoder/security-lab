using Microsoft.AspNetCore.Builder;
using Platform.Access.AspNet;
using Platform.Access.Domain.Constants;

namespace Platform.Audit.Endpoints;

/// <summary>
/// Route and Access metadata of the course audit endpoint (AUD-06, S-14), mapped by
/// <c>MapCourseAuditEndpoints()</c> with <see cref="CourseAuditEndpointHandler"/>.
/// </summary>
internal static class CourseAuditEndpointRoute
{
    public const string CourseIdParameter = "courseId";

    /// <summary>The <c>guid</c> constraint keeps malformed IDs from ever reaching the handler.</summary>
    public const string Pattern = "/api/courses/{" + CourseIdParameter + ":guid}/audit-events";

    public static string For(Guid courseId) => $"/api/courses/{courseId:D}/audit-events";

    /// <summary>
    /// Declares <c>audit.events.read</c> on <c>course/{courseId}</c>, so AccessControlMiddleware
    /// decides before the handler runs. The resource type comes from the central Access catalog,
    /// the ID only from the route value.
    /// </summary>
    public static TBuilder RequireCourseAuditRead<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAccess(
            AccessActions.AuditEventsRead.Id,
            AccessActions.AuditEventsRead.ResourceType!,
            CourseIdParameter);
}
