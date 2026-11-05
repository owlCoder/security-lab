using System.Runtime.ExceptionServices;

namespace Platform.Access.Infrastructure.Auditing;

internal sealed class AggregatingAccessAuditSink(
    IAccessAuditSink inner,
    AccessAuditOptions options,
    TimeProvider clock) : IAccessAuditSink
{
    private readonly object _gate = new();
    private readonly Dictionary<Key, Window> _windows = [];

    private DateTimeOffset _nextExpiry = DateTimeOffset.MaxValue;

    private readonly List<AccessAuditRecord> _pending = [];

    public async ValueTask WriteAsync(AccessAuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.EventType != AccessAuditRecord.DeniedEventType)
        {
            await inner.WriteAsync(record, cancellationToken).ConfigureAwait(false);
            return;
        }

        var key = new Key(record.Actor, record.Action, record.Target, record.DenialReason);
        var now = clock.GetUtcNow();
        List<AccessAuditRecord> summaries;
        Window? opened = null;
        var writeNow = false;

        lock (_gate)
        {
            summaries = CloseWindows(now, includeOpen: false);

            if (_windows.TryGetValue(key, out var window))
            {
                window.Held += Math.Max(1, record.Count);
                window.Last = record;
            }
            else if (_windows.Count < options.MaxAggregationKeys)
            {
                writeNow = true;
                opened = Open(key, now);
            }
            else
            {
                key = Key.OverflowOf(record.DenialReason);
                if (_windows.TryGetValue(key, out var overflow))
                {
                    overflow.Held += Math.Max(1, record.Count);
                    overflow.Last = record;
                }
                else
                {
                    writeNow = true;
                    if (_windows.Count < 2L * options.MaxAggregationKeys)
                    {
                        opened = Open(key, now);
                    }
                }
            }
        }

        var summaryFailure = await WriteSummariesAsync(summaries, cancellationToken).ConfigureAwait(false);

        if (writeNow)
        {
            try
            {
                await inner.WriteAsync(record, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Forget(key, opened);
                throw;
            }
        }

        summaryFailure?.Throw();
    }

    public ValueTask FlushExpiredAsync(CancellationToken cancellationToken = default) =>
        FlushAsync(includeOpen: false, cancellationToken);

    public ValueTask FlushAllAsync(CancellationToken cancellationToken = default) =>
        FlushAsync(includeOpen: true, cancellationToken);

    private async ValueTask FlushAsync(bool includeOpen, CancellationToken cancellationToken)
    {
        List<AccessAuditRecord> summaries;
        lock (_gate)
        {
            var closed = CloseWindows(clock.GetUtcNow(), includeOpen);
            summaries = [.. _pending, .. closed];
            _pending.Clear();
        }

        (await WriteSummariesAsync(summaries, cancellationToken).ConfigureAwait(false))?.Throw();
    }

    private async ValueTask<ExceptionDispatchInfo?> WriteSummariesAsync(
        List<AccessAuditRecord> summaries,
        CancellationToken cancellationToken)
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var summary in summaries)
        {
            try
            {
                await inner.WriteAsync(summary, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure ??= ExceptionDispatchInfo.Capture(ex);
                Requeue(summary);
            }
        }

        return failure;
    }

    private void Requeue(AccessAuditRecord summary)
    {
        lock (_gate)
        {
            if (_pending.Count < options.MaxAggregationKeys)
            {
                _pending.Add(summary);
            }
        }
    }

    private Window Open(Key key, DateTimeOffset now)
    {
        var window = new Window(now);
        _windows[key] = window;

        var end = now + options.DenyAggregationWindow;
        if (end < _nextExpiry)
        {
            _nextExpiry = end;
        }

        return window;
    }

    private List<AccessAuditRecord> CloseWindows(DateTimeOffset now, bool includeOpen)
    {
        List<AccessAuditRecord> summaries = [];

        if (!includeOpen && now < _nextExpiry)
        {
            return summaries;
        }

        List<Key>? expired = null;
        var nextExpiry = DateTimeOffset.MaxValue;

        foreach (var (key, window) in _windows)
        {
            var end = window.Started + options.DenyAggregationWindow;
            if (!includeOpen && now < end)
            {
                if (end < nextExpiry)
                {
                    nextExpiry = end;
                }

                continue;
            }

            (expired ??= []).Add(key);
            if (window.Held > 0 && window.Last is not null)
            {
                summaries.Add(SummaryOf(key, window));
            }
        }

        if (expired is not null)
        {
            foreach (var key in expired)
            {
                _windows.Remove(key);
            }
        }

        _nextExpiry = nextExpiry;
        return summaries;
    }

    private static AccessAuditRecord SummaryOf(Key key, Window window)
    {
        var summary = window.Last! with { Count = window.Held };

        return key.IsOverflow
            ? summary with
            {
                Actor = AccessAuditRecord.NoValue,
                Action = AccessAuditRecord.NoValue,
                Target = AccessAuditRecord.NoValue,
                CorrelationId = AccessAuditRecord.NoValue,
            }
            : summary;
    }

    private void Forget(Key key, Window? opened)
    {
        if (opened is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_windows.TryGetValue(key, out var current) && ReferenceEquals(current, opened))
            {
                _windows.Remove(key);

                if (opened.Held > 0 && opened.Last is not null && _pending.Count < options.MaxAggregationKeys)
                {
                    _pending.Add(SummaryOf(key, opened));
                }
            }
        }
    }

    private readonly record struct Key(
        string Actor, string Action, string Target, string? DenialReason, bool IsOverflow = false)
    {
        public static Key OverflowOf(string? denialReason) =>
            new(AccessAuditRecord.NoValue, AccessAuditRecord.NoValue, AccessAuditRecord.NoValue, denialReason, true);
    }

    private sealed class Window(DateTimeOffset started)
    {
        public DateTimeOffset Started { get; } = started;

        public int Held { get; set; }

        public AccessAuditRecord? Last { get; set; }
    }
}
