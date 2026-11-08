using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Platform.Access.AspNet;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Application;
using Platform.Audit.Contracts;

namespace Platform.Audit.Tests.CourseView;

// Test doubles for S-14. None of them is registered by Platform.Api.

/// <summary>Access decision with a fixed answer; records every request it receives.</summary>
internal sealed class ScriptedAccessDecision : IAccessDecision
{
    private readonly ConcurrentQueue<AccessRequest> requests = new();
    public IReadOnlyList<AccessRequest> Requests => requests.ToArray();
    public Func<AccessRequest, CancellationToken, ValueTask<AccessDecision>> Handler { get; set; } =
        (request, _) => ValueTask.FromResult(AccessDecision.Allow("audit.course.read", request.CorrelationId, "v1"));

    public ValueTask<AccessDecision> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        requests.Enqueue(request);
        return Handler(request, cancellationToken);
    }
}

/// <summary>
/// Trusted writer that runs the real <see cref="AuditDraftValidator"/>, then answers as scripted.
/// A draft the real writer would reject is answered as Rejected, as in production.
/// </summary>
internal sealed class ValidatingTrustedAuditWriter : ITrustedAuditWriter
{
    private readonly AuditDraftValidator validator = new();
    private readonly ConcurrentQueue<(AuditEventDraft Draft, AuditContext Context)> written = new();
    public IReadOnlyList<(AuditEventDraft Draft, AuditContext Context)> Written => written.ToArray();
    public int Calls => calls;
    private int calls;

    public Func<AuditEventDraft, AuditContext, CancellationToken, ValueTask<AuditWriteResult>>? Handler { get; set; }

    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft, AuditContext context, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref calls);
        var rejection = validator.Validate(draft, context);
        if (rejection != AuditRejectionCode.None)
            return ValueTask.FromResult(AuditWriteResult.Rejected(rejection));

        if (Handler is not null)
            return Handler(draft, context, cancellationToken);

        written.Enqueue((draft, context));
        return ValueTask.FromResult(AuditWriteResult.Recorded(Guid.NewGuid()));
    }
}

/// <summary>
/// Test-only subject source: <c>X-Test-Subject: {guid}</c> is an active account,
/// <c>blocked:{guid}</c> a blocked one, any other value a non-account subject. No header is anonymous.
/// </summary>
internal sealed class HeaderTestSubjectProvider : IAccessSubjectProvider
{
    public const string HeaderName = "X-Test-Subject";

    public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        string? value = httpContext.Request.Headers[HeaderName];
        if (string.IsNullOrEmpty(value))
            return ValueTask.FromResult<SubjectRef?>(null);

        return ValueTask.FromResult<SubjectRef?>(value.StartsWith("blocked:", StringComparison.Ordinal)
            ? new SubjectRef(value["blocked:".Length..], AccountState.Blocked)
            : new SubjectRef(value, AccountState.Active));
    }
}

internal sealed class CourseViewCapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<(LogLevel Level, EventId EventId, string Text)> entries = new();
    public IReadOnlyList<(LogLevel Level, EventId EventId, string Text)> Entries => entries.ToArray();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        entries.Enqueue((logLevel, eventId, formatter(state, exception) + (exception is null ? "" : " " + exception)));
}
