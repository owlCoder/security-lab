using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Api.Contracts.Audit;
using Platform.Audit.Contracts;

namespace Platform.Api.Controllers.Auditing;

/// <summary>
/// AUD-06: a teacher's view of the security events of ONE course.
/// <list type="bullet">
/// <item>Access: <c>audit.events.read</c> on <c>course/{courseId}</c>, decided by the Access
/// middleware before this code runs (teacher, or assistant with a delegation; default deny).</item>
/// <item>The viewer is taken only from the server-validated session; the course in the data
/// query is the authorized route value, never a body or query value.</item>
/// <item>The view is recorded (<c>audit.events.viewed</c>) before data is returned; if that fails
/// the response is 503 and carries no events.</item>
/// <item>There is deliberately NO HTTP endpoint that writes audit events: actor, time and
/// correlation ID must come from the server, so producers call <see cref="IAuditWriter"/> in code.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/audit/courses/{courseId:guid}/events")]
[RequireAccess("audit.events.read", ResourceType = "course", IdRouteParam = "courseId")]
public sealed class AuditEventsController(
    IAuditCourseReader reader,
    IAuditActorSource actors,
    ICorrelationContext correlation) : ControllerBase
{
    private static readonly string[] UtcFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"
    ];

    [HttpGet]
    public async Task<IActionResult> List(
        Guid courseId,
        [FromQuery] string? eventType,
        [FromQuery] string? outcome,
        [FromQuery(Name = "from")] string? fromUtc,
        [FromQuery(Name = "to")] string? toUtc,
        [FromQuery] string? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!TryBuildQuery(eventType, outcome, fromUtc, toUtc, limit, cursor, out var query))
            return BadRequest(new { code = "invalid_query" });

        var actor = actors.GetCurrentActor();
        if (actor is not { Kind: AuditActorKind.User, Id: Guid viewerId } || viewerId == Guid.Empty
            || string.IsNullOrWhiteSpace(correlation.CorrelationId))
            return StatusCode(StatusCodes.Status401Unauthorized, new { code = "unauthenticated" });

        var viewer = new AuditContext(actor, correlation.CorrelationId);
        var result = await reader.ReadCourseEventsAsync(courseId, viewer, query, cancellationToken);

        return result.Status switch
        {
            AuditReadStatus.Ok when result.Page is { } page => Ok(ToResponse(page)),
            AuditReadStatus.InvalidQuery => BadRequest(new { code = "invalid_query" }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "audit_unavailable" })
        };
    }

    private static bool TryBuildQuery(
        string? eventType, string? outcome, string? fromUtc, string? toUtc, string? limit, string? cursor,
        out AuditEventQuery query)
    {
        query = new AuditEventQuery();

        AuditOutcome? parsedOutcome = null;
        if (outcome is not null)
        {
            // Letters only: Enum.TryParse would also accept numbers such as "1".
            if (outcome.Length is 0 or > 16 || !outcome.All(char.IsAsciiLetter)
                || !Enum.TryParse<AuditOutcome>(outcome, ignoreCase: true, out var value)
                || !Enum.IsDefined(value))
                return false;
            parsedOutcome = value;
        }

        if (!TryParseUtc(fromUtc, out var parsedFrom) || !TryParseUtc(toUtc, out var parsedTo))
            return false;

        int? parsedLimit = null;
        if (limit is not null)
        {
            if (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return false;
            parsedLimit = number;
        }

        if (eventType is { Length: > 64 } || cursor is { Length: > 64 })
            return false;

        query = new AuditEventQuery(eventType, parsedOutcome, parsedFrom, parsedTo, parsedLimit, cursor);
        return true;
    }

    // Absent is fine; present must be an explicit UTC timestamp ending with 'Z'.
    private static bool TryParseUtc(string? raw, out DateTimeOffset? value)
    {
        value = null;
        if (raw is null)
            return true;

        if (raw.Length > 40
            || !DateTimeOffset.TryParseExact(raw, UtcFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return false;

        value = parsed;
        return true;
    }

    private static AuditEventsPageResponse ToResponse(AuditEventPage page) => new(
        page.Items.Select(item => new AuditEventResponse(
            item.Id,
            item.OccurredAt,
            new AuditActorResponse(item.ActorKind.ToString().ToLowerInvariant(), item.ActorId),
            item.EventType,
            item.Outcome.ToString().ToLowerInvariant(),
            item.ResourceType is null || item.ResourceId is null
                ? null
                : new AuditResourceResponse(item.ResourceType, item.ResourceId),
            item.CorrelationId,
            item.Details)).ToList(),
        page.NextCursor);
}
