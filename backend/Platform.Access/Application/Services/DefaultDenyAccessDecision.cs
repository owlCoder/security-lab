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

        if (definition.Scope == ActionScope.Unknown ||
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

        var permitted = await EvaluatePolicyAsync(policy, request, cancellationToken).ConfigureAwait(false);

        return permitted
            ? AccessDecision.Allow(policyId, correlationId, policyVersion)
            : AccessDecision.Deny(AccessDenialReason.PolicyDenied, correlationId, policyVersion);
    }

    private async Task<bool> EvaluatePolicyAsync(
        IAccessPolicy policy,
        AccessRequest request,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(options.PolicyTimeout);

        try
        {
            var task = policy.EvaluateAsync(request, linked.Token).AsTask();
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
        try
        {
            var task = auditWriter.WriteAsync(request, decision, CancellationToken.None).AsTask();
            ObserveFault(task);
            await task.WaitAsync(options.AuditTimeout).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this.logger.LogError(exception, "Authorization audit failed. CorrelationId: {CorrelationId}", decision.CorrelationId);
        }
    }

    private static void ObserveFault(Task task) =>
        task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static string NormalizeCorrelationId(string? correlationId) =>
        string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : Regex.Replace(correlationId, @"[^\u0021-\u007E]", "_")[..Math.Min(correlationId.Length, 64)];
}