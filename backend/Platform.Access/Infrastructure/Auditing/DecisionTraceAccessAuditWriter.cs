using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.Infrastructure.Auditing;

public sealed partial class DecisionTraceAccessAuditWriter(
    IAccessAuditWriter inner,
    IActionCatalog catalog,
    IDecisionTraceFormatter formatter,
    ILogger<DecisionTraceAccessAuditWriter> logger) : IAccessAuditWriter
{
    private const string Rejected = "invalid";

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    [GeneratedRegex(@"^[a-z][a-z0-9_.:-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyIdPattern();

    [GeneratedRegex(@"^v?[0-9]{1,4}(\.[0-9]{1,4}){0,2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyVersionPattern();

    public ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken = default)
    {
        LogPrivilegedAllow(request, decision, trace: null);
        return inner.WriteAsync(request, decision, cancellationToken);
    }

    public ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        DecisionTrace trace,
        CancellationToken cancellationToken = default)
    {
        if (decision.Outcome == AccessOutcome.Allow)
        {
            LogPrivilegedAllow(request, decision, trace);
        }
        else
        {
            LogDeniedTrace(
                logger,
                SafeAction(request.Action),
                decision.Outcome,
                SafeCorrelationId(decision.CorrelationId),
                formatter.Format(trace));
        }

        return inner.WriteAsync(request, decision, trace, cancellationToken);
    }

    private void LogPrivilegedAllow(AccessRequest request, AccessDecision decision, DecisionTrace? trace)
    {
        if (decision.Outcome != AccessOutcome.Allow ||
            !ActionId.IsValid(request.Action) ||
            !catalog.TryGet(request.Action, out var definition) ||
            definition is null ||
            !definition.IsPrivileged)
        {
            return;
        }

        LogPrivilegedAllowed(
            logger,
            request.Action,
            SafeSubject(request.Subject),
            SafePolicyId(decision.PolicyId),
            SafePolicyVersion(decision.PolicyVersion),
            SafeCorrelationId(decision.CorrelationId),
            formatter.Format(trace));
    }

    private static string SafeAction(string? action) =>
        ActionId.IsValid(action) ? action! : Rejected;

    private static string SafeCorrelationId(string? correlationId) =>
        correlationId is not null && CorrelationIdPattern().IsMatch(correlationId) ? correlationId : Rejected;

    private static string SafePolicyId(string? policyId) =>
        policyId is not null && PolicyIdPattern().IsMatch(policyId) ? policyId : Rejected;

    private static string SafePolicyVersion(string? version) =>
        version is not null && PolicyVersionPattern().IsMatch(version) ? version : Rejected;

    private static string SafeSubject(SubjectRef? subject)
    {
        if (subject is null)
        {
            return "anonymous";
        }

        return Guid.TryParseExact(subject.Id, "D", out var id) && id != Guid.Empty
            ? id.ToString("D")
            : Rejected;
    }

    [LoggerMessage(
        EventId = 6103,
        EventName = "access.privileged.allowed",
        Level = LogLevel.Information,
        Message = "access.privileged.allowed action={Action} subjectId={SubjectId} policyId={PolicyId} policyVersion={PolicyVersion} correlationId={CorrelationId} trace={Trace}")]
    private static partial void LogPrivilegedAllowed(
        ILogger logger, string action, string subjectId, string policyId, string policyVersion,
        string correlationId, string trace);

    [LoggerMessage(
        EventId = 6104,
        EventName = "access.decision.trace",
        Level = LogLevel.Information,
        Message = "access.decision.trace action={Action} outcome={Outcome} correlationId={CorrelationId} trace={Trace}")]
    private static partial void LogDeniedTrace(
        ILogger logger, string action, AccessOutcome outcome, string correlationId, string trace);
}
