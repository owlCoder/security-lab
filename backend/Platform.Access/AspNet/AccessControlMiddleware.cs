using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.AspNet;

public sealed class AccessControlMiddleware
{
    private static readonly TimeSpan DecisionTimeout = TimeSpan.FromSeconds(3);

    private readonly RequestDelegate _next;
    private readonly ILogger<AccessControlMiddleware> _logger;

    public AccessControlMiddleware(RequestDelegate next, ILogger<AccessControlMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAccessDecision decider,
        IAccessSubjectProvider subjectProvider,
        IAccessAuditWriter auditWriter)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint is null)
        {
            await _next(context);   
            return;
        }

        var requirements = endpoint.Metadata.GetOrderedMetadata<IAccessRequirementMetadata>();
        var requirement = requirements.Count == 1 ? requirements[0] : null;
#pragma warning disable CS0618 // The middleware intentionally continues rejecting legacy markers during migration.
        var isPublic = endpoint.Metadata.GetMetadata<PublicEndpointMetadata>() is not null
                       || endpoint.Metadata.GetMetadata<AllowPublicAttribute>() is not null;
        var delegated = endpoint.Metadata.GetMetadata<DelegatedAuthorizationAttribute>() is not null;
#pragma warning restore CS0618

        var correlationId = context.TraceIdentifier;
        if (requirement is null || isPublic || delegated)
        {
            _logger.LogWarning(
                "Access denied for endpoint {Endpoint}: {Reason}. Replace legacy/public authorization metadata with exactly one RequireAccess action and a registered policy. CorrelationId: {CorrelationId}",
                endpoint.DisplayName,
                requirement is null ? "missing-or-ambiguous-requirement" : "legacy-or-public-metadata",
                correlationId);
            await AuditMiddlewareDenialAsync(
                auditWriter,
                new AccessRequest(null, requirement?.Action ?? string.Empty, null, correlationId),
                AccessDecision.Deny(AccessDenialReason.PolicyError, correlationId));
            await AccessProblemDetailsWriter.WriteAsync(context, unauthenticated: false, correlationId);
            return;
        }

        SubjectRef? subject;
        try
        {
            subject = await subjectProvider.GetSubjectAsync(context, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw; 
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subject resolution failed. CorrelationId: {CorrelationId}", correlationId);
            await AuditMiddlewareDenialAsync(
                auditWriter,
                new AccessRequest(null, requirement.Action, null, correlationId),
                AccessDecision.Deny(AccessDenialReason.PolicyError, correlationId));
            await AccessProblemDetailsWriter.WriteAsync(context, unauthenticated: false, correlationId);
            return;
        }

        var unauthenticatedSubject = subject is null;

        if (!ActionId.IsValid(requirement.Action))
        {
            _logger.LogError(
                "Invalid action declared by endpoint {Endpoint}. CorrelationId: {CorrelationId}",
                endpoint.DisplayName, correlationId);
            await AuditMiddlewareDenialAsync(
                auditWriter,
                new AccessRequest(subject, requirement.Action, null, correlationId),
                AccessDecision.Deny(AccessDenialReason.PolicyError, correlationId));
            await AccessProblemDetailsWriter.WriteAsync(context, unauthenticatedSubject, correlationId);
            return;
        }

        ResourceRef? resource = null;

        if (requirement.ResourceType is not null)
        {
            if (!AccessMetadataRules.IsValidResourceType(requirement.ResourceType)
                || string.IsNullOrWhiteSpace(requirement.IdRouteParam)
                || !context.Request.RouteValues.TryGetValue(requirement.IdRouteParam, out var rawId)
                || !AccessMetadataRules.TryNormalizeResourceId(
                       Convert.ToString(rawId, System.Globalization.CultureInfo.InvariantCulture),
                       out var resourceId))
            {
                await AuditMiddlewareDenialAsync(
                    auditWriter,
                    new AccessRequest(subject, requirement.Action, null, correlationId),
                    AccessDecision.Deny(AccessDenialReason.PolicyError, correlationId));
                await AccessProblemDetailsWriter.WriteAsync(context, unauthenticatedSubject, correlationId);
                return;
            }

            resource = new ResourceRef(requirement.ResourceType, resourceId);
        }
        else if (requirement.IdRouteParam is not null)
        {
            await AuditMiddlewareDenialAsync(
                auditWriter,
                new AccessRequest(subject, requirement.Action, null, correlationId),
                AccessDecision.Deny(AccessDenialReason.PolicyError, correlationId));
            await AccessProblemDetailsWriter.WriteAsync(context, unauthenticatedSubject, correlationId);
            return;
        }

        AccessDecision? decision;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            cts.CancelAfter(DecisionTimeout);

            var request = new AccessRequest(subject, requirement.Action, resource, correlationId);
            decision = await decider.DecideAsync(request, cts.Token);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)   
        {
            _logger.LogError(ex, "Access decision failed. Access denied. CorrelationId: {CorrelationId}", correlationId);
            await AuditMiddlewareDenialAsync(
                auditWriter,
                new AccessRequest(subject, requirement.Action, resource, correlationId),
                AccessDecision.Deny(AccessDenialReason.PolicyError, correlationId));
            await AccessProblemDetailsWriter.WriteAsync(context, unauthenticatedSubject, correlationId);
            return;
        }

        if (decision is null || decision.Outcome != AccessOutcome.Allow)
        {
            if (decision is null || decision.Outcome != AccessOutcome.Deny)
            {
                await AuditMiddlewareDenialAsync(
                    auditWriter,
                    new AccessRequest(subject, requirement.Action, resource, correlationId),
                    AccessDecision.Deny(
                        AccessDenialReason.PolicyError,
                        correlationId,
                        decision?.PolicyVersion ?? "v0",
                        decision?.PolicyId));
            }

            var unauthenticated = unauthenticatedSubject
                                  || decision?.DenialReason == AccessDenialReason.Unauthenticated;

            _logger.LogInformation(
                "Access denied for endpoint {Endpoint}. CorrelationId: {CorrelationId}",
                endpoint.DisplayName, correlationId);

            await AccessProblemDetailsWriter.WriteAsync(context, unauthenticated, correlationId);
            return; 
        }

        await _next(context);
    }

    private async Task AuditMiddlewareDenialAsync(
        IAccessAuditWriter auditWriter,
        AccessRequest request,
        AccessDecision decision)
    {
        try
        {
            await auditWriter.WriteAsync(request, decision, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authorization audit failed. CorrelationId: {CorrelationId}", decision.CorrelationId);
        }
    }
}
