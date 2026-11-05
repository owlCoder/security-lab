using System.Globalization;
using System.Text.RegularExpressions;
using Platform.Audit.Contracts;

namespace Platform.Audit.Application;

/// <summary>Validated course query: every value is parsed, bounded and in UTC.</summary>
internal sealed record NormalizedCourseAuditQuery(
    Guid CourseId,
    string? EventType,
    AuditOutcome? Outcome,
    Guid? ActorId,
    DateTimeOffset From,
    DateTimeOffset To,
    string? CorrelationId,
    int Page,
    int PageSize)
{
    public int Offset => (Page - 1) * PageSize;
}

/// <summary>
/// Turns raw filters into a <see cref="NormalizedCourseAuditQuery"/> or a list of errors (S-07).
/// Stateless apart from the clock. Rejected values are never copied into an error.
/// Rules (proposal D5): page 1 and size 50 by default, size 1-100, offset at most 10 000;
/// ISO 8601 times with an explicit zone, converted to UTC; <c>from</c> inclusive, <c>to</c>
/// exclusive and <c>from &lt; to</c>; no dates means the last 7 days; only one date means a
/// 7-day window next to it; the range is at most 31 days.
/// </summary>
internal sealed partial class CourseAuditQueryNormalizer(TimeProvider timeProvider)
{
    private static readonly string[] OutcomeNames = ["success", "denied", "failure"];

    // Same characters as the correlation ID in the audit contract. \z, not $: $ also accepts a final newline.
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    // Date and time with a mandatory zone: Z or +hh:mm / -hh:mm. No date-only or local times.
    [GeneratedRegex(
        @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]{1,7})?)?(Z|[+-][0-9]{2}:[0-9]{2})\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"^[0-9]{1,6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public IReadOnlyList<CourseAuditQueryError> Normalize(
        CourseAuditQuery query, out NormalizedCourseAuditQuery? normalized)
    {
        ArgumentNullException.ThrowIfNull(query);
        normalized = null;
        var errors = new List<CourseAuditQueryError>();

        if (query.CourseId == Guid.Empty)
            errors.Add(new(CourseAuditQueryField.CourseId, CourseAuditQueryErrorCode.Required));

        var eventType = ParseType(query.Type, errors);
        var outcome = ParseOutcome(query.Outcome, errors);
        var actorId = ParseActorId(query.ActorId, errors);
        var correlationId = ParseCorrelationId(query.CorrelationId, errors);
        var from = ParseTimestamp(query.From, CourseAuditQueryField.From, errors);
        var to = ParseTimestamp(query.To, CourseAuditQueryField.To, errors);
        var page = ParseNumber(query.Page, CourseAuditQueryField.Page, CourseAuditQueryLimits.DefaultPage, errors);
        var pageSize = ParseNumber(query.PageSize, CourseAuditQueryField.PageSize,
            CourseAuditQueryLimits.DefaultPageSize, errors);

        if (page is not null && page < 1)
        {
            errors.Add(new(CourseAuditQueryField.Page, CourseAuditQueryErrorCode.OutOfRange));
            page = null;
        }

        if (pageSize is not null
            && (pageSize < CourseAuditQueryLimits.MinPageSize || pageSize > CourseAuditQueryLimits.MaxPageSize))
        {
            errors.Add(new(CourseAuditQueryField.PageSize, CourseAuditQueryErrorCode.OutOfRange));
            pageSize = null;
        }

        if (page is not null && pageSize is not null
            && (long)(page.Value - 1) * pageSize.Value > CourseAuditQueryLimits.MaxOffset)
            errors.Add(new(CourseAuditQueryField.Page, CourseAuditQueryErrorCode.OutOfRange));

        var range = ResolveRange(query.From, from, query.To, to, errors);

        if (errors.Count > 0)
            return errors;

        normalized = new NormalizedCourseAuditQuery(
            query.CourseId, eventType, outcome, actorId, range!.Value.From, range.Value.To,
            correlationId, page!.Value, pageSize!.Value);
        return errors;
    }

    private (DateTimeOffset From, DateTimeOffset To)? ResolveRange(
        string? rawFrom, DateTimeOffset? from, string? rawTo, DateTimeOffset? to,
        List<CourseAuditQueryError> errors)
    {
        // A date that was given but is invalid is already reported; do not invent a range for it.
        if ((IsSet(rawFrom) && from is null) || (IsSet(rawTo) && to is null))
            return null;

        var window = CourseAuditQueryLimits.DefaultRange;

        // A one-sided window must stay inside the DateTimeOffset range, or adding it would throw.
        if ((from is { } onlyFrom && to is null && onlyFrom > DateTimeOffset.MaxValue - window)
            || (to is { } onlyTo && from is null && onlyTo < DateTimeOffset.MinValue + window))
        {
            errors.Add(new(CourseAuditQueryField.Range, CourseAuditQueryErrorCode.OutOfRange));
            return null;
        }

        (DateTimeOffset From, DateTimeOffset To) range = (from, to) switch
        {
            (null, null) => Last(window),
            ({ } f, null) => (f, f + window),
            (null, { } t) => (t - window, t),
            ({ } f, { } t) => (f, t)
        };

        if (range.From >= range.To || range.To - range.From > CourseAuditQueryLimits.MaxRange)
        {
            errors.Add(new(CourseAuditQueryField.Range, CourseAuditQueryErrorCode.OutOfRange));
            return null;
        }

        return range;
    }

    private (DateTimeOffset From, DateTimeOffset To) Last(TimeSpan window)
    {
        var now = _timeProvider.GetUtcNow().ToUniversalTime();
        return (now - window, now);
    }

    private static string? ParseType(string? value, List<CourseAuditQueryError> errors)
    {
        if (!IsSet(value))
            return null;

        if (AuditEventCatalog.TryGet(value, out var schema))
            return schema.EventType;

        errors.Add(new(CourseAuditQueryField.Type, CourseAuditQueryErrorCode.UnknownValue));
        return null;
    }

    private static AuditOutcome? ParseOutcome(string? value, List<CourseAuditQueryError> errors)
    {
        if (!IsSet(value))
            return null;

        // Lower-case names only, as in the audit.events.viewed detail; no numbers, no other spelling.
        switch (Array.IndexOf(OutcomeNames, value))
        {
            case 0: return AuditOutcome.Success;
            case 1: return AuditOutcome.Denied;
            case 2: return AuditOutcome.Failure;
            default:
                errors.Add(new(CourseAuditQueryField.Outcome, CourseAuditQueryErrorCode.UnknownValue));
                return null;
        }
    }

    private static Guid? ParseActorId(string? value, List<CourseAuditQueryError> errors)
    {
        if (!IsSet(value))
            return null;

        if (Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty)
            return id;

        errors.Add(new(CourseAuditQueryField.ActorId, CourseAuditQueryErrorCode.InvalidFormat));
        return null;
    }

    private static string? ParseCorrelationId(string? value, List<CourseAuditQueryError> errors)
    {
        if (!IsSet(value))
            return null;

        if (CorrelationIdPattern().IsMatch(value))
            return value;

        errors.Add(new(CourseAuditQueryField.CorrelationId, CourseAuditQueryErrorCode.InvalidFormat));
        return null;
    }

    private static DateTimeOffset? ParseTimestamp(
        string? value, CourseAuditQueryField field, List<CourseAuditQueryError> errors)
    {
        if (!IsSet(value))
            return null;

        if (value.Length <= CourseAuditQueryLimits.MaxTimestampLength
            && TimestampPattern().IsMatch(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed.ToUniversalTime();

        errors.Add(new(field, CourseAuditQueryErrorCode.InvalidFormat));
        return null;
    }

    private static int? ParseNumber(
        string? value, CourseAuditQueryField field, int defaultValue, List<CourseAuditQueryError> errors)
    {
        if (!IsSet(value))
            return defaultValue;

        if (value.Length <= CourseAuditQueryLimits.MaxNumberLength
            && NumberPattern().IsMatch(value)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return number;

        errors.Add(new(field, CourseAuditQueryErrorCode.InvalidFormat));
        return null;
    }

    private static bool IsSet([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value) =>
        !string.IsNullOrEmpty(value);
}
