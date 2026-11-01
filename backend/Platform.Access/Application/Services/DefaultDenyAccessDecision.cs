using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Application.Services;

public sealed class DefaultDenyAccessDecision(
    IActionCatalog actionCatalog,
    IAccessPolicyRegistry policyRegistry,
    IAccessAuditWriter auditWriter) : IAccessDecision
{
    public async ValueTask<AccessDecision> DecideAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var decision = await EvaluateAsync(request, cancellationToken);
        await auditWriter.WriteAsync(request, decision, cancellationToken);
        return decision;
    }

    private async ValueTask<AccessDecision> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Subject is null)
        {
            return AccessDecision.Deny(
                AccessDenialReason.Unauthenticated,
                request.CorrelationId);
        }

        if (request.Subject.State != AccountState.Active)
        {
            return AccessDecision.Deny(
                AccessDenialReason.AccountNotActive,
                request.CorrelationId);
        }

        if (!actionCatalog.Contains(request.Action))
        {
            return AccessDecision.Deny(
                AccessDenialReason.UnknownAction,
                request.CorrelationId);
        }

        if (!policyRegistry.TryGet(request.Action, out var policy) || policy is null)
        {
            return AccessDecision.Deny(
                AccessDenialReason.NoPolicy,
                request.CorrelationId);
        }

        try
        {
            var permitted = await policy.EvaluateAsync(request, cancellationToken);
            return permitted
                ? AccessDecision.Allow(policy.Id, request.CorrelationId, policy.Version)
                : AccessDecision.Deny(
                    AccessDenialReason.PolicyError,
                    request.CorrelationId,
                    policy.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AccessDecision.Deny(
                AccessDenialReason.PolicyError,
                request.CorrelationId,
                policy.Version);
        }
    }
}
