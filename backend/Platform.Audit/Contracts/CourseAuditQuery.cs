namespace Platform.Audit.Contracts;

/// <summary>
/// Course audit view request (AUD-06, S-07). <see cref="CourseId"/> is mandatory and comes from the
/// route, never from a filter; there is no query without a course. The other values are the raw
/// query-string filters as received (null or empty means "not set"); they are parsed, bounded and
/// normalized by the query service. This type carries no authorization: the caller (S-14/S-15) must
/// authorize <c>audit.events.read</c> on <c>Course/{courseId}</c> before calling the service.
/// </summary>
public sealed record CourseAuditQuery(
    Guid CourseId,
    string? Type = null,
    string? Outcome = null,
    string? ActorId = null,
    string? From = null,
    string? To = null,
    string? CorrelationId = null,
    string? Page = null,
    string? PageSize = null);

/// <summary>Limits of the course audit query. Proposal D5 in DECISIONS.md.</summary>
public static class CourseAuditQueryLimits
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 100;

    /// <summary>Upper bound for (page - 1) * pageSize; deeper pages are refused (T11).</summary>
    public const int MaxOffset = 10_000;

    /// <summary>Range used when no date is given, and the length of the window when only one date is given.</summary>
    public static readonly TimeSpan DefaultRange = TimeSpan.FromDays(7);

    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(31);

    /// <summary>Longest accepted raw value of a date filter, for example 2026-11-05T12:00:00.1234567+01:00.</summary>
    public const int MaxTimestampLength = 40;

    public const int MaxNumberLength = 6;
}

/// <summary>Filter that failed validation. Stable names, used in a 400 response.</summary>
public enum CourseAuditQueryField
{
    CourseId,
    Type,
    Outcome,
    ActorId,
    From,
    To,
    Range,
    CorrelationId,
    Page,
    PageSize
}

/// <summary>Why a filter was refused. The rejected value itself is never returned or logged.</summary>
public enum CourseAuditQueryErrorCode
{
    Required,
    InvalidFormat,
    UnknownValue,
    OutOfRange
}

public sealed record CourseAuditQueryError(CourseAuditQueryField Field, CourseAuditQueryErrorCode Code);

/// <summary>
/// One event as a teacher may see it. Allowlist: <c>details</c>, the schema version and the course
/// are not included (the course is the one in the request). New fields need a decision (D4).
/// </summary>
public sealed record CourseAuditEventDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string EventType,
    AuditOutcome Outcome,
    AuditActorKind ActorKind,
    Guid? ActorId,
    string? ResourceType,
    string? ResourceId,
    string CorrelationId);

/// <summary>
/// One page, sorted by <c>OccurredAt DESC, Id DESC</c>. There is no total count: <see cref="HasNextPage"/>
/// comes from reading one extra row, and is false when the next page would exceed
/// <see cref="CourseAuditQueryLimits.MaxOffset"/>. Pages are not a snapshot; new events can shift later pages.
/// <see cref="From"/> (inclusive) and <see cref="To"/> (exclusive) are the normalized UTC range.
/// </summary>
public sealed record CourseAuditPage(
    Guid CourseId,
    DateTimeOffset From,
    DateTimeOffset To,
    int Page,
    int PageSize,
    bool HasNextPage,
    IReadOnlyList<CourseAuditEventDto> Items);

public enum CourseAuditQueryStatus
{
    Ok,

    /// <summary>One or more filters are invalid (400). No database call was made.</summary>
    Invalid,

    /// <summary>Storage failed (503). No partial data and no internal details.</summary>
    Unavailable
}

public sealed record CourseAuditQueryResult
{
    private CourseAuditQueryResult(
        CourseAuditQueryStatus status, CourseAuditPage? page, IReadOnlyList<CourseAuditQueryError> errors)
    {
        Status = status;
        Page = page;
        Errors = errors;
    }

    public CourseAuditQueryStatus Status { get; }

    public CourseAuditPage? Page { get; }

    public IReadOnlyList<CourseAuditQueryError> Errors { get; }

    public static CourseAuditQueryResult Ok(CourseAuditPage page) =>
        new(CourseAuditQueryStatus.Ok, page ?? throw new ArgumentNullException(nameof(page)), []);

    public static CourseAuditQueryResult Invalid(IReadOnlyList<CourseAuditQueryError> errors) =>
        errors is { Count: > 0 }
            ? new(CourseAuditQueryStatus.Invalid, null, errors)
            : throw new ArgumentException("An invalid result needs at least one error.", nameof(errors));

    public static CourseAuditQueryResult Unavailable { get; } = new(CourseAuditQueryStatus.Unavailable, null, []);
}

/// <summary>
/// Reads the events of exactly one course. There is no global or course-less mode. Authorization
/// is not done here (S-08/S-15); the endpoint must authorize before calling this service.
/// </summary>
public interface ICourseAuditQueryService
{
    Task<CourseAuditQueryResult> QueryAsync(CourseAuditQuery query, CancellationToken cancellationToken = default);
}
