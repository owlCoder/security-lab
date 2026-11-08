using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Platform.Audit.Contracts;

namespace Platform.Audit.Application.CourseView;

/// <summary>
/// Safe summary of one successful course audit view (AUD-06, S-14). It holds the normalized filter
/// range and the number of returned events, never the events themselves. The actor and course are
/// the ones the authorizer confirmed and the route supplied; nothing here comes from the HTTP body.
/// <see cref="EventType"/> and <see cref="Outcome"/> are null when the filter was not set.
/// The <c>actorId</c> and <c>correlationId</c> filters are not part of the catalog schema for
/// <c>audit.events.viewed</c>, so they are not recorded (catalog change needs D5/D6 agreement).
/// </summary>
internal sealed record CourseAuditViewSummary(
    Guid ActorId,
    Guid CourseId,
    string CorrelationId,
    string? EventType,
    AuditOutcome? Outcome,
    DateTimeOffset From,
    DateTimeOffset To,
    int ResultCount);

/// <summary>
/// Writes <c>audit.events.viewed</c> through <see cref="ITrustedAuditWriter"/> before any event is
/// sent to the viewer. <see cref="RecordAsync"/> returns false for every failure (invalid summary,
/// Rejected, Unavailable, exception); the endpoint then answers 503 without data (D5, provisional).
/// It never throws except for caller cancellation, and it does not call <c>IAccessDecision</c>, so
/// recording a view cannot trigger another access decision or <c>access.denied</c> record (S-13).
/// </summary>
internal sealed partial class CourseAuditViewRecorder(
    ITrustedAuditWriter writer,
    ILogger<CourseAuditViewRecorder> logger)
{
    internal const int AlarmEventId = 6301;
    internal const string InvalidCorrelationPlaceholder = "invalid";

    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private readonly ITrustedAuditWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly ILogger<CourseAuditViewRecorder> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    public async ValueTask<bool> RecordAsync(
        CourseAuditViewSummary summary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (!TryCreate(summary, out var draft, out var context))
        {
            Alarm("invalid_summary", nameof(AuditRejectionCode.None), summary.CorrelationId);
            return false;
        }

        AuditWriteResult? result;
        try
        {
            result = await _writer.WriteAsync(draft, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Only the exception type: its message can contain SQL or connection details.
            Alarm("failed", exception.GetType().Name, summary.CorrelationId);
            return false;
        }

        if (result is { IsRecorded: true })
            return true;

        if (result is { Status: AuditWriteStatus.Rejected })
            Alarm("rejected", result.RejectionCode.ToString(), summary.CorrelationId);
        else
            Alarm("unavailable", nameof(AuditWriteStatus.Unavailable), summary.CorrelationId);

        return false;
    }

    /// <summary>
    /// Maps a summary to the catalog shape of <c>audit.events.viewed</c>: User actor, required course
    /// and Course resource, and only the allowed detail keys. Final validation stays in the writer.
    /// </summary>
    internal static bool TryCreate(
        CourseAuditViewSummary summary,
        out AuditEventDraft draft,
        out AuditContext context)
    {
        draft = null!;
        context = null!;

        if (summary.ActorId == Guid.Empty
            || summary.CourseId == Guid.Empty
            || summary.From >= summary.To
            || summary.ResultCount < 0
            || summary.CorrelationId is null
            || !CorrelationIdPattern().IsMatch(summary.CorrelationId)
            || (summary.EventType is not null && !AuditEventCatalog.TryGet(summary.EventType, out _))
            || (summary.Outcome is { } outcome && !Enum.IsDefined(outcome)))
        {
            return false;
        }

        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["filterFrom"] = FormatUtc(summary.From),
            ["filterTo"] = FormatUtc(summary.To),
            ["resultCount"] = summary.ResultCount.ToString(CultureInfo.InvariantCulture)
        };

        if (summary.EventType is not null)
            details["filterType"] = summary.EventType;

        if (summary.Outcome is { } filterOutcome)
            details["filterOutcome"] = OutcomeName(filterOutcome);

        var course = summary.CourseId.ToString("D");
        draft = new AuditEventDraft(
            AuditEventTypes.AuditEventsViewed,
            AuditOutcome.Success,
            summary.CourseId,
            new AuditResourceRef(AuditResourceTypes.Course, course),
            details);
        context = new AuditContext(AuditActor.ForUser(summary.ActorId), summary.CorrelationId);
        return true;
    }

    internal static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture);

    // Same lower-case names as the S-07 outcome filter and AuditDetailFormat.OutcomeName.
    internal static string OutcomeName(AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Success => "success",
        AuditOutcome.Denied => "denied",
        AuditOutcome.Failure => "failure",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    private void Alarm(string reason, string code, string? correlationId)
    {
        try
        {
            LogViewNotRecorded(
                _logger,
                reason,
                code,
                correlationId is not null && CorrelationIdPattern().IsMatch(correlationId)
                    ? correlationId
                    : InvalidCorrelationPlaceholder);
        }
        catch (Exception)
        {
            // A failing logger must not turn the 503 path into an unhandled exception.
        }
    }

    [LoggerMessage(
        EventId = AlarmEventId,
        Level = LogLevel.Error,
        Message = "audit.events.viewed.failure reason={Reason} code={Code} correlationId={CorrelationId}")]
    private static partial void LogViewNotRecorded(ILogger logger, string reason, string code, string correlationId);
}
