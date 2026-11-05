using Platform.Audit.Contracts;
using Platform.Audit.Domain;

namespace Platform.Audit.Application;

/// <summary>
/// Validates a draft, enriches it only with trusted server data and UTC time, then
/// hands the immutable event to append-only storage (AUD-01/S-06).
/// </summary>
internal sealed class AuditWriter : IAuditWriter, ITrustedAuditWriter
{
    private readonly AuditDraftValidator _validator;
    private readonly IAuditContextProvider _contextProvider;
    private readonly IAuditEventStore _store;
    private readonly TimeProvider _timeProvider;

    public AuditWriter(
        AuditDraftValidator validator,
        IAuditContextProvider contextProvider,
        IAuditEventStore store,
        TimeProvider timeProvider)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var context = _contextProvider.GetCurrentContext();
        return context is null
            ? ValueTask.FromResult(AuditWriteResult.Rejected(AuditRejectionCode.InvalidContext))
            : WriteCoreAsync(draft, context, cancellationToken);
    }

    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        AuditContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(context);

        return WriteCoreAsync(draft, context, cancellationToken);
    }

    private async ValueTask<AuditWriteResult> WriteCoreAsync(
        AuditEventDraft draft,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        // This gate enforces the catalog, outcome/actor rules, detail allowlists,
        // flat string values, 20-key limit and 2048-byte UTF-8 JSON limit. It runs
        // before the clock and store so rejected input has no external side effect.
        var rejection = _validator.Validate(draft, context);
        if (rejection != AuditRejectionCode.None)
            return AuditWriteResult.Rejected(rejection);

        if (draft.EventId == Guid.Empty)
            return AuditWriteResult.Rejected(AuditRejectionCode.EventIdConflict);

        cancellationToken.ThrowIfCancellationRequested();

        var eventId = draft.EventId ?? Guid.NewGuid();
        var occurredAt = _timeProvider.GetUtcNow().ToUniversalTime();
        var auditEvent = AuditEvent.Create(eventId, occurredAt, draft, context);

        try
        {
            var storeResult = await _store
                .AppendAsync(auditEvent, cancellationToken)
                .ConfigureAwait(false);

            return storeResult switch
            {
                AuditAppendStatus.Appended or AuditAppendStatus.AlreadyPresent =>
                    AuditWriteResult.Recorded(eventId),
                AuditAppendStatus.Conflict =>
                    AuditWriteResult.Rejected(AuditRejectionCode.EventIdConflict),
                _ => AuditWriteResult.Unavailable()
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Never copy storage errors, actor data or rejected details into the result.
            return AuditWriteResult.Unavailable();
        }
    }
}

/// <summary>
/// Supplies actor and correlation data from trusted server infrastructure. An
/// implementation must not construct this context from an HTTP body or query string.
/// </summary>
internal interface IAuditContextProvider
{
    AuditContext? GetCurrentContext();
}
