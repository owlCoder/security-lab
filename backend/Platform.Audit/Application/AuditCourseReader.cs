using System.Globalization;
using Microsoft.Extensions.Logging;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;

namespace Platform.Audit.Application;

/// <summary>
/// AUD-06 read use case. Authorization of <c>audit.events.read</c> is a precondition checked by
/// the host (Platform.Access). This class reads only events of ONE course and records the view
/// as <c>audit.events.viewed</c> BEFORE returning any data; if that record is not stored, no
/// data is returned (fail closed, decision D5 proposal in docs/components/audit/DECISIONS.md).
/// Logs contain only event type, status and correlation ID, never filters, details or exceptions.
/// </summary>
internal sealed class AuditCourseReader(
    IAuditEventQueryStore store,
    ITrustedAuditWriter writer,
    ILogger<AuditCourseReader> logger) : IAuditCourseReader
{
    // Matches the UtcTimestamp detail format: seconds, optional fraction, trailing 'Z'.
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public async ValueTask<AuditReadResult> ReadCourseEventsAsync(
        Guid courseId,
        AuditContext viewer,
        AuditEventQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(query);

        if (courseId == Guid.Empty || !TryNormalize(query, out var filter, out var limit))
            return AuditReadResult.InvalidQuery();

        // A view is always attributed to a confirmed person. Anything else is a host defect.
        if (viewer.Actor is not { Kind: AuditActorKind.User, Id: Guid viewerId } || viewerId == Guid.Empty)
        {
            logger.LogError("Audit view refused: viewer is not a confirmed user. CorrelationId: {CorrelationId}",
                SafeCorrelation(viewer));
            return AuditReadResult.Unavailable();
        }

        IReadOnlyList<AuditEvent> rows;
        try
        {
            // One extra row tells whether a next page exists.
            rows = await store.QueryCourseAsync(courseId, filter, limit + 1, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("Audit course read failed ({ErrorType}). CorrelationId: {CorrelationId}",
                ex.GetType().Name, SafeCorrelation(viewer));
            return AuditReadResult.Unavailable();
        }

        var pageRows = rows.Take(limit).ToList();
        var nextCursor = rows.Count > limit
            ? new AuditCursor(pageRows[^1].OccurredAt, pageRows[^1].Id).Format()
            : null;

        var draft = new AuditEventDraft(
            AuditEventTypes.AuditEventsViewed,
            AuditOutcome.Success,
            courseId,
            new AuditResourceRef(AuditResourceTypes.Course, courseId.ToString("D")),
            BuildViewDetails(filter, pageRows.Count));

        AuditWriteResult written;
        try
        {
            written = await writer.WriteAsync(draft, viewer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("Audit view record failed ({ErrorType}). CorrelationId: {CorrelationId}",
                ex.GetType().Name, SafeCorrelation(viewer));
            return AuditReadResult.Unavailable();
        }

        if (!written.IsRecorded)
        {
            logger.LogError(
                "Audit view was not recorded ({Status}/{Rejection}); no data returned. CorrelationId: {CorrelationId}",
                written.Status, written.RejectionCode, SafeCorrelation(viewer));
            return AuditReadResult.Unavailable();
        }

        var items = pageRows.Select(ToView).ToList();
        return AuditReadResult.Ok(new AuditEventPage(items, nextCursor));
    }

    private static bool TryNormalize(AuditEventQuery query, out AuditEventFilter filter, out int limit)
    {
        filter = null!;
        limit = 0;

        var requested = query.Limit ?? AuditReadLimits.DefaultLimit;
        if (requested < 1 || requested > AuditReadLimits.MaxLimit)
            return false;

        if (query.EventType is not null && !AuditEventCatalog.TryGet(query.EventType, out _))
            return false;

        if (query.Outcome is { } outcome && !Enum.IsDefined(outcome))
            return false;

        if (query.From is { } fromValue && fromValue.Offset != TimeSpan.Zero)
            return false;

        if (query.To is { } toValue && toValue.Offset != TimeSpan.Zero)
            return false;

        if (query.From is { } lower && query.To is { } upper && lower > upper)
            return false;

        AuditCursor? after = null;
        if (query.Cursor is not null)
        {
            if (!AuditCursor.TryParse(query.Cursor, out var parsed))
                return false;
            after = parsed;
        }

        filter = new AuditEventFilter(query.EventType, query.Outcome, query.From, query.To, after);
        limit = requested;
        return true;
    }

    private static Dictionary<string, string> BuildViewDetails(AuditEventFilter filter, int resultCount)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["resultCount"] = resultCount.ToString(CultureInfo.InvariantCulture)
        };

        if (filter.EventType is not null)
            details["filterType"] = filter.EventType;
        if (filter.Outcome is { } outcome)
            details["filterOutcome"] = outcome.ToString().ToLowerInvariant();
        if (filter.From is { } fromValue)
            details["filterFrom"] = FormatUtc(fromValue);
        if (filter.To is { } toValue)
            details["filterTo"] = FormatUtc(toValue);

        return details;
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static AuditEventView ToView(AuditEvent record) => new(
        record.Id,
        record.OccurredAt,
        record.ActorKind,
        record.ActorId,
        record.EventType,
        record.Outcome,
        record.CourseId,
        record.ResourceType,
        record.ResourceId,
        record.CorrelationId,
        new Dictionary<string, string>(record.Details, StringComparer.Ordinal));

    // The correlation ID is server-generated; still never log anything else from the context.
    private static string SafeCorrelation(AuditContext viewer) =>
        viewer.CorrelationId is { Length: > 0 and <= 64 } id ? id : "unknown";
}
