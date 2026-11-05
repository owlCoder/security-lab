using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
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
        IAccessSubjectProvider subjectProvider)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint is null)
        {
            await _next(context);   
            return;
        }

        var requirement = endpoint.Metadata.GetMetadata<IAccessRequirementMetadata>();
        var isPublic = endpoint.Metadata.GetMetadata<PublicEndpointMetadata>() is not null
                       || endpoint.Metadata.GetMetadata<AllowPublicAttribute>() is not null;
        var delegated = endpoint.Metadata.GetMetadata<DelegatedAuthorizationAttribute>() is not null;

        // Explicitly reviewed legacy endpoints retain their own tested authorization.
        // Conflicting or missing metadata fails closed as before.
        if (requirement is null && (isPublic != delegated))
        {
            await _next(context);
            return;
        }

        var correlationId = Guid.NewGuid().ToString("N");   
        if (requirement is null || isPublic || delegated)
        {
            _logger.LogWarning(
                "Fallback deny for endpoint {Endpoint} (requirement: {HasRequirement}, public: {IsPublic}). CorrelationId: {CorrelationId}",
                endpoint.DisplayName, requirement is not null, isPublic, correlationId);

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
            await AccessProblemDetailsWriter.WriteAsync(context, unauthenticated: false, correlationId);
            return;
        }

        var unauthenticatedSubject = subject is null;

        if (!ActionId.IsValid(requirement.Action))
        {
            _logger.LogError(
                "Invalid action declared by endpoint {Endpoint}. CorrelationId: {CorrelationId}",
                endpoint.DisplayName, correlationId);

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
                await AccessProblemDetailsWriter.WriteAsync(context, unauthenticatedSubject, correlationId);
                return;
            }

            resource = new ResourceRef(requirement.ResourceType, resourceId);
        }
        else if (requirement.IdRouteParam is not null)
        {
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
            await AccessProblemDetailsWriter.WriteAsync(context, unauthenticatedSubject, correlationId);
            return;
        }

        if (decision is null || decision.Outcome != AccessOutcome.Allow)
        {
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
}
