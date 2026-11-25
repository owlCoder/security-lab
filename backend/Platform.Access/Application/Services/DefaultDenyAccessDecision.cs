using Platform.Access.Application.Options;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Platform.Access.Application.Services;

public sealed class DefaultDenyAccessDecision : IAccessDecision
{
    private readonly IActionCatalog actionCatalog;
    private readonly IAccessPolicyRegistry policyRegistry;
    private readonly IAccessAuditWriter auditWriter;
    private readonly AccessDecisionOptions options;
    private readonly ILogger<DefaultDenyAccessDecision> logger;

    public DefaultDenyAccessDecision(
        IActionCatalog actionCatalog,
        IAccessPolicyRegistry policyRegistry,
        IAccessAuditWriter auditWriter,
        AccessDecisionOptions options,
        ILogger<DefaultDenyAccessDecision>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(actionCatalog);
        ArgumentNullException.ThrowIfNull(policyRegistry);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        this.actionCatalog = actionCatalog;
        this.policyRegistry = policyRegistry;
        this.auditWriter = auditWriter;
        this.options = options;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultDenyAccessDecision>.Instance;
    }

    public async ValueTask<AccessDecision> DecideAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = NormalizeCorrelationId(request?.CorrelationId);
        request ??= new AccessRequest(null, string.Empty, null, correlationId);

        AccessDecision decision;
        try
        {
            decision = await EvaluateAsync(request, correlationId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this.logger.LogError(exception, "Authorization evaluation failed; access denied. CorrelationId: {CorrelationId}", correlationId);
            decision = AccessDecision.Deny(AccessDenialReason.PolicyError, correlationId);
        }

        await TryAuditAsync(request, decision).ConfigureAwait(false);
        return decision;
    }

    private async ValueTask<AccessDecision> EvaluateAsync(
        AccessRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!actionCatalog.TryGet(request.Action, out var definition) || definition is null)
        {
            return AccessDecision.Deny(AccessDenialReason.UnknownAction, correlationId);
        }

        if (!Enum.IsDefined(definition.Scope) ||
            (definition.ResourceType is null && request.Resource is not null) ||
            (definition.ResourceType is not null &&
             (request.Resource is null ||
              !string.Equals(definition.ResourceType, request.Resource.Type, StringComparison.Ordinal))))
        {
            return AccessDecision.Deny(AccessDenialReason.PolicyError, correlationId);
        }

        if (definition.Scope != ActionScope.Public)
        {
            if (request.Subject is null)
            {
                return AccessDecision.Deny(AccessDenialReason.Unauthenticated, correlationId);
            }

            if (request.Subject.State != AccountState.Active)
            {
                return AccessDecision.Deny(AccessDenialReason.AccountNotActive, correlationId);
            }
        }

        if (!policyRegistry.TryGet(request.Action, out var policy) || policy is null)
        {
            return AccessDecision.Deny(AccessDenialReason.NoPolicy, correlationId);
        }

        var policyId = policy.Id;
        var policyVersion = policy.Version;
        if (string.IsNullOrWhiteSpace(policyId) || string.IsNullOrWhiteSpace(policyVersion))
        {
            return AccessDecision.Deny(AccessDenialReason.PolicyError, correlationId);
        }

        if (policy is IOutcomeAccessPolicy outcomePolicy)
        {
            var result = await RunPolicyAsync(
                token => outcomePolicy.EvaluateOutcomeAsync(request, token),
                cancellationToken).ConfigureAwait(false);

            return ToDecision(result, policyId, policyVersion, correlationId);
        }

        var permitted = await RunPolicyAsync(
            token => policy.EvaluateAsync(request, token),
            cancellationToken).ConfigureAwait(false);
        return permitted
            ? AccessDecision.Allow(policyId, correlationId, policyVersion)
            : AccessDecision.Deny(AccessDenialReason.PolicyDenied, correlationId, policyVersion, policyId);
    }

    private static AccessDecision ToDecision(
        PolicyResult? result,
        string policyId,
        string policyVersion,
        string correlationId)
    {
        if (result is null || !Enum.IsDefined(result.Outcome))
        {
            return AccessDecision.Deny(AccessDenialReason.PolicyError, correlationId, policyVersion);
        }

        switch (result.Outcome)
        {
            case PolicyOutcome.Permit:
                return result.Reason == AccessDenialReason.None
                    ? AccessDecision.Allow(policyId, correlationId, policyVersion)
                    : AccessDecision.Deny(AccessDenialReason.PolicyError, correlationId, policyVersion);

            case PolicyOutcome.Indeterminate:
                return AccessDecision.Deny(
                    PolicyReason(result.Reason, AccessDenialReason.MissingAttribute),
                    correlationId,
                    policyVersion,
                    policyId);

            default:
                return AccessDecision.Deny(
                    PolicyReason(result.Reason, AccessDenialReason.NotPermitted),
                    correlationId,
                    policyVersion,
                    policyId);
        }
    }

    private static AccessDenialReason PolicyReason(AccessDenialReason reason, AccessDenialReason fallback) =>
        reason is AccessDenialReason.NotPermitted
            or AccessDenialReason.ContextMismatch
            or AccessDenialReason.MissingAttribute
            or AccessDenialReason.PolicyDenied
            or AccessDenialReason.PolicyError
            ? reason
            : fallback;

    private async Task<T> RunPolicyAsync<T>(
        Func<CancellationToken, ValueTask<T>> evaluate,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(options.PolicyTimeout);

        try
        {
            var task = evaluate(linked.Token).AsTask();
            ObserveFault(task);
            return await task.WaitAsync(options.PolicyTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            linked.Cancel();
        }
    }

    private async Task TryAuditAsync(AccessRequest request, AccessDecision decision)
    {
        using var timeout = new CancellationTokenSource(options.AuditTimeout);

        try
        {
            var task = auditWriter.WriteAsync(request, decision, timeout.Token).AsTask();
            ObserveFault(task);
            await task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            LogAuditLost("timeout", decision.CorrelationId);
        }
        catch (Exception)
        {
            LogAuditLost("failed", decision.CorrelationId);
        }
    }

    private void LogAuditLost(string status, string correlationId) =>
        logger.LogError(
            AccessAuditEventIds.Lost,
            "Access denial audit was not saved. EventType: {EventType}, Status: {Status}, CorrelationId: {CorrelationId}",
            AccessAuditEventIds.DeniedEventType,
            status,
            correlationId);

    private static void ObserveFault(Task task) =>
        task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static string NormalizeCorrelationId(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return Guid.NewGuid().ToString("N");
        }

        var bounded = correlationId[..Math.Min(correlationId.Length, 64)];
        return Regex.Replace(bounded, @"[^\u0021-\u007E]", "_");
    }
}