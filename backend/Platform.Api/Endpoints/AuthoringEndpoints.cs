using Platform.Access.AspNet;
using Microsoft.AspNetCore.Mvc;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using AuthoringAccessDecision = Platform.Access.Contracts.IAccessDecision;
using Platform.Authoring.Application;
using Platform.Authoring.Domain;

namespace Platform.Api.Endpoints;

public sealed record TemplateResponse(Guid Id, string Name, string Title, string Goal, string Type);

public sealed record TemplateListResponse(IReadOnlyList<TemplateResponse> Templates, string CorrelationId);

public sealed record DraftResponse(
    Guid Id,
    Guid? SourceTemplateId,
    Guid OwnerId,
    string Status,
    int Version,
    string Title,
    string Goal,
    string Type,
    string CorrelationId);

public sealed record AuthoringErrorResponse(string Error, string CorrelationId);

/// <summary>
/// AUT-06 endpoints: list the laboratory templates and create a draft from one.
/// Every request is decided by the platform access module first (the caller comes from the server-side
/// subject resolver, never from the request), and the use case checks authorization again.
/// </summary>
public static class AuthoringEndpoints
{
    public static IEndpointRouteBuilder MapAuthoringEndpoints(this IEndpointRouteBuilder app)
    {
        // Both handlers call AuthorizeAsync and the authoring service rechecks access.
        app.MapGet("/api/authoring/templates", ListTemplatesAsync)
            .DelegatesToLegacyAuthorization();
        app.MapPost("/api/authoring/templates/{templateId:guid}/drafts", CopyTemplateAsync)
            .DelegatesToLegacyAuthorization();
        return app;
    }

    public static async Task<IResult> ListTemplatesAsync(
        HttpContext context,
        //IRequestSubjectResolver subjectResolver,
        [FromKeyedServices("AuthoringSession")] IRequestSubjectResolver subjectResolver,
        AuthoringAccessDecision accessDecision,
        [FromServices] TemplateCatalogService catalog,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        var (denied, actorId) = await AuthorizeAsync(
            context, subjectResolver, accessDecision, AuthoringActions.TemplateList, null, correlationId, cancellationToken);
        if (denied is not null) return denied;

        try
        {
            var templates = await catalog.ListAsync(actorId, cancellationToken);
            return Results.Ok(new TemplateListResponse(
                templates.Select(ToResponse).ToList(),
                correlationId));
        }
        catch (DomainRuleException ex)
        {
            return MapError(ex, correlationId);
        }
    }

    public static async Task<IResult> CopyTemplateAsync(
        Guid templateId,
        HttpContext context,
        //IRequestSubjectResolver subjectResolver,
        [FromKeyedServices("AuthoringSession")] IRequestSubjectResolver subjectResolver,
        AuthoringAccessDecision accessDecision,
        [FromServices] DraftService drafts,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        var (denied, actorId) = await AuthorizeAsync(
            context,
            subjectResolver,
            accessDecision,
            AuthoringActions.TemplateCopy,
            new ResourceRef(AuthoringActions.TemplateResourceType, templateId.ToString("D")),
            correlationId,
            cancellationToken);
        if (denied is not null) return denied;

        try
        {
            var draft = await drafts.CreateDraftFromTemplateAsync(actorId, templateId, cancellationToken);
            return Results.Json(ToResponse(draft, correlationId), statusCode: StatusCodes.Status201Created);
        }
        catch (DomainRuleException ex)
        {
            return MapError(ex, correlationId);
        }
    }

    private static async Task<(IResult? Denied, Guid ActorId)> AuthorizeAsync(
        HttpContext context,
        IRequestSubjectResolver subjectResolver,
        AuthoringAccessDecision accessDecision,
        string action,
        ResourceRef? resource,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var subject = subjectResolver.Resolve(context);

        var decision = await accessDecision.DecideAsync(
            new AccessRequest(subject, action, resource, correlationId),
            cancellationToken);

        if (decision.Outcome != AccessOutcome.Allow)
        {
            return (decision.DenialReason == AccessDenialReason.Unauthenticated
                ? Error(StatusCodes.Status401Unauthorized, "unauthenticated", correlationId)
                : Error(StatusCodes.Status403Forbidden, "forbidden", correlationId),
                Guid.Empty);
        }

        // The owner of a new draft is always the authenticated account, never a value from the request.
        if (subject is null || !Guid.TryParse(subject.Id, out var actorId) || actorId == Guid.Empty)
            return (Error(StatusCodes.Status403Forbidden, "forbidden", correlationId), Guid.Empty);

        return (null, actorId);
    }

    private static IResult MapError(DomainRuleException ex, string correlationId) => ex.Code switch
    {
        AuthoringErrorCodes.Forbidden => Error(StatusCodes.Status403Forbidden, "forbidden", correlationId),
        AuthoringErrorCodes.NotFound => Error(StatusCodes.Status404NotFound, "template_not_found", correlationId),
        AuthoringErrorCodes.Conflict => Error(StatusCodes.Status409Conflict, "conflict", correlationId),
        _ => Error(StatusCodes.Status400BadRequest, "invalid_request", correlationId),
    };

    private static IResult Error(int statusCode, string error, string correlationId) =>
        Results.Json(new AuthoringErrorResponse(error, correlationId), statusCode: statusCode);

    private static TemplateResponse ToResponse(LabTemplate t) =>
        new(t.Id, t.Name, t.Content.Title, t.Content.Goal, t.Content.Type.ToString());

    private static DraftResponse ToResponse(LabDraft d, string correlationId) =>
        new(d.Id, d.SourceTemplateId, d.OwnerId, d.Status.ToString(), d.Version,
            d.Content.Title, d.Content.Goal, d.Content.Type.ToString(), correlationId);
}
