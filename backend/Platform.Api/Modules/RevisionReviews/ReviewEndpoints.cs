using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Security.RevisionAuthorization;

namespace Platform.Api.Modules.RevisionReviews;

public static class ReviewEndpoints
{
    private const string EntryAction = RevisionActions.Entry;

    public static IServiceCollection AddRevisionReviews(this IServiceCollection services)
    {
        services.AddAccessAction<RevisionEntryPolicy>(
            new ActionDefinition(
                EntryAction,
                ActionScope.Authenticated,
                null,
                false));
        services.TryAddScoped<IPublicationSource, StoredPublicationSource>();
        services.AddScoped<ReviewService>();
        services.AddScoped<DraftRevisionService>();
        return services;
    }

    public static IEndpointRouteBuilder MapRevisionReviews(this IEndpointRouteBuilder app)
    {
        // The central middleware requires an active account. The services then check each
        // operation on its concrete resource, including course IDs in the body and list rows.
        var group = app.MapGroup("/api/revisions").WithTags("Revision review").RequireAccess(EntryAction);
        group.MapPost("/", (CreateReviewDraft? body, HttpContext http, IRequestSubjectResolver subjects,
            DraftRevisionService drafts, CancellationToken ct) =>
        {
            if (Resolve(http, subjects) is not { } caller)
                return Task.FromResult(ReviewService.Error(401, "unauthenticated"));
            if (body?.Definition is not { } definition)
                return Task.FromResult(ReviewService.Error(400, "invalid_input"));
            return drafts.CreateAsync(body.LaboratoryId, body.CourseId, caller.Id, caller.Subject,
                definition, http.TraceIdentifier, ct);
        }).WithMetadata(new RequestSizeLimitAttribute(196608));
        group.MapGet("/pending-reviews", (string? courseId, HttpContext http, IRequestSubjectResolver subjects,
            ReviewService service, CancellationToken ct) =>
        {
            if (Resolve(http, subjects) is not { } caller)
                return Task.FromResult(ReviewService.Error(401, "unauthenticated"));
            if (courseId is not null && !Guid.TryParse(courseId, out _))
                return Task.FromResult(ReviewService.Error(400, "invalid_input"));
            Guid? course = courseId is null ? null : Guid.Parse(courseId);
            return service.ListAsync(course, caller.Id, caller.Subject, true, http.TraceIdentifier, ct);
        });
        group.MapGet("/mine", (HttpContext http, IRequestSubjectResolver subjects, ReviewService service, CancellationToken ct) =>
            Resolve(http, subjects) is { } caller
                ? service.ListAsync(null, caller.Id, caller.Subject, false, http.TraceIdentifier, ct)
                : Task.FromResult(ReviewService.Error(401, "unauthenticated")));
        group.MapGet("/{id:guid}", (Guid id, HttpContext http, IRequestSubjectResolver subjects,
            ReviewService service, CancellationToken ct) =>
            Resolve(http, subjects) is { } caller
                ? service.GetAsync(id, caller.Subject, http.TraceIdentifier, ct)
                : Task.FromResult(ReviewService.Error(401, "unauthenticated")));

        foreach (var action in new[] { "submit", "approve", "return" })
        {
            group.MapPost("/{id:guid}/" + action, (Guid id, ReviewBody? body,
                [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http,
                IRequestSubjectResolver subjects, ReviewService service, CancellationToken ct) =>
            {
                if (Resolve(http, subjects) is not { } caller)
                    return Task.FromResult(ReviewService.Error(401, "unauthenticated"));
                if (ReviewCommand.Parse(action, body, key ?? "") is not { } command)
                    return Task.FromResult(ReviewService.Error(400, "invalid_input"));
                return service.ApplyAsync(id, caller.Id, caller.Subject, command, http.TraceIdentifier, ct);
            }).WithMetadata(new RequestSizeLimitAttribute(8192));
        }
        return app;
    }

    private static (Guid Id, SubjectRef Subject)? Resolve(HttpContext http, IRequestSubjectResolver subjects)
    {
        var subject = subjects.Resolve(http);
        return subject is { State: AccountState.Active } && Guid.TryParse(subject.Id, out var id) && id != Guid.Empty
            ? (id, subject) : null;
    }

    private sealed class RevisionEntryPolicy : IAccessPolicy
    {
        public string Id => EntryAction;
        public string Version => "1";

        public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(request.Subject is { State: AccountState.Active } && request.Resource is null);
    }
}
