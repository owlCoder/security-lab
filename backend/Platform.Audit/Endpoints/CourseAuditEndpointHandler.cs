using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Platform.Access.AspNet;
using Platform.Audit.Application.CourseView;
using Platform.Audit.Contracts;

namespace Platform.Audit.Endpoints;

/// <summary>
/// <c>GET /api/courses/{courseId}/audit-events</c> (AUD-06, S-14). The order is fixed:
/// <list type="number">
/// <item>authorize the route course (second check; the middleware already decided once),</item>
/// <item>validate the filters and read one page through the S-07 query service,</item>
/// <item>record <c>audit.events.viewed</c>,</item>
/// <item>only then send the page.</item>
/// </list>
/// A caller without the right gets 401/403 before any filter is looked at, so a refused filter
/// never tells an outsider anything. The course comes only from the route.
/// </summary>
internal static partial class CourseAuditEndpointHandler
{
    internal const int QueryUnavailableEventId = 6302;

    /// <summary>Query-string names accepted by the endpoint; anything else is ignored.</summary>
    internal static readonly string[] FilterNames =
        ["type", "outcome", "actorId", "from", "to", "correlationId", "page", "pageSize"];

    // Enums as lower-case names ("denied", "user"), the same spelling as the outcome filter.
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static async Task<IResult> HandleAsync(
        Guid courseId,
        HttpContext http,
        ICorrelationContext correlation,
        IAccessSubjectProvider subjects,
        CourseAuditReadAuthorizer authorizer,
        ICourseAuditQueryService queries,
        CourseAuditViewRecorder recorder,
        ILoggerFactory loggerFactory)
    {
        var cancellationToken = http.RequestAborted;

        // S-10 assigns it as the first middleware; without it nothing can be recorded, so nothing is sent.
        if (correlation.CorrelationId is not { Length: > 0 } correlationId)
            return CourseAuditProblemResults.Unavailable(CourseAuditViewRecorder.InvalidCorrelationPlaceholder);

        var authorization = await AuthorizeAsync(http, subjects, authorizer, courseId, correlationId, cancellationToken);
        if (!authorization.IsAllowed)
        {
            return CourseAuditProblemResults.AccessDenied(
                authorization.Status == CourseAuditReadStatus.Unauthenticated, correlationId);
        }

        if (!TryReadFilters(http.Request.Query, courseId, out var query, out var repeated))
            return CourseAuditProblemResults.InvalidFilters(repeated, correlationId);

        CourseAuditQueryResult result;
        try
        {
            result = await queries.QueryAsync(query, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogQueryUnavailable(Logger(loggerFactory), exception.GetType().Name, correlationId);
            return CourseAuditProblemResults.Unavailable(correlationId);
        }

        switch (result.Status)
        {
            case CourseAuditQueryStatus.Invalid:
                return CourseAuditProblemResults.InvalidFilters(result.Errors.Select(ToFilterError), correlationId);

            case CourseAuditQueryStatus.Ok when result.Page is { } page:
                var recorded = await recorder.RecordAsync(
                    new CourseAuditViewSummary(
                        authorization.ActorId,
                        courseId,
                        correlationId,
                        string.IsNullOrEmpty(query.Type) ? null : query.Type,
                        ParseOutcome(query.Outcome),
                        page.From,
                        page.To,
                        page.Items.Count),
                    cancellationToken);

                // D5 (provisional): no record of the view, no data.
                return recorded
                    ? new CourseAuditPageResult(page)
                    : CourseAuditProblemResults.Unavailable(correlationId);

            default:
                LogQueryUnavailable(Logger(loggerFactory), nameof(CourseAuditQueryStatus.Unavailable), correlationId);
                return CourseAuditProblemResults.Unavailable(correlationId);
        }
    }

    private static async ValueTask<CourseAuditReadAuthorization> AuthorizeAsync(
        HttpContext http,
        IAccessSubjectProvider subjects,
        CourseAuditReadAuthorizer authorizer,
        Guid courseId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        Platform.Access.Domain.Models.SubjectRef? subject;
        try
        {
            subject = await subjects.GetSubjectAsync(http, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Same as AccessControlMiddleware: a subject that cannot be resolved is refused.
            return CourseAuditReadAuthorization.Forbidden;
        }

        return await authorizer.AuthorizeAsync(subject, courseId, correlationId, cancellationToken);
    }

    /// <summary>
    /// Raw filters for S-07, which parses and bounds them. A filter given more than once is refused
    /// here, because S-07 receives one value per filter. The course is the route value, never a filter.
    /// </summary>
    internal static bool TryReadFilters(
        IQueryCollection queryString,
        Guid courseId,
        out CourseAuditQuery query,
        out IReadOnlyList<CourseAuditFilterError> repeated)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var errors = new List<CourseAuditFilterError>();
        foreach (var name in FilterNames)
        {
            var value = queryString.TryGetValue(name, out var raw) ? raw : StringValues.Empty;
            if (value.Count > 1)
                errors.Add(new CourseAuditFilterError(name, nameof(CourseAuditQueryErrorCode.InvalidFormat)));
            values[name] = value.Count == 1 ? value[0] : null;
        }

        repeated = errors;
        query = new CourseAuditQuery(
            courseId,
            values["type"],
            values["outcome"],
            values["actorId"],
            values["from"],
            values["to"],
            values["correlationId"],
            values["page"],
            values["pageSize"]);
        return errors.Count == 0;
    }

    internal static CourseAuditFilterError ToFilterError(CourseAuditQueryError error) =>
        new(JsonNamingPolicy.CamelCase.ConvertName(error.Field.ToString()), error.Code.ToString());

    // Called only after S-07 accepted the filter, so the value is one of these names.
    private static AuditOutcome? ParseOutcome(string? value) => value switch
    {
        "success" => AuditOutcome.Success,
        "denied" => AuditOutcome.Denied,
        "failure" => AuditOutcome.Failure,
        _ => null
    };

    private static ILogger Logger(ILoggerFactory factory) => factory.CreateLogger(typeof(CourseAuditEndpointHandler));

    [LoggerMessage(
        EventId = QueryUnavailableEventId,
        Level = LogLevel.Error,
        Message = "audit.course_view.unavailable code={Code} correlationId={CorrelationId}")]
    private static partial void LogQueryUnavailable(ILogger logger, string code, string correlationId);

    /// <summary>200 with the S-07 page: allowlisted DTOs only, never cached.</summary>
    private sealed class CourseAuditPageResult(CourseAuditPage page) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            httpContext.Response.Headers.CacheControl = "no-store";
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, page, Json, httpContext.RequestAborted);
        }
    }
}
