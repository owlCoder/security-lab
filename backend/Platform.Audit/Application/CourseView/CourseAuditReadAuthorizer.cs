using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Audit.Application.CourseView;

internal enum CourseAuditReadStatus
{
    Allowed,

    /// <summary>401: no authenticated subject.</summary>
    Unauthenticated,

    /// <summary>403: authenticated, but no current right for this course (or the check failed).</summary>
    Forbidden
}

/// <summary>
/// Result of the S-14 application-layer check. <see cref="ActorId"/> is set only when allowed and is
/// the account recorded as the actor of <c>audit.events.viewed</c>.
/// </summary>
internal sealed record CourseAuditReadAuthorization(CourseAuditReadStatus Status, Guid ActorId)
{
    public static CourseAuditReadAuthorization Unauthenticated { get; } =
        new(CourseAuditReadStatus.Unauthenticated, Guid.Empty);

    public static CourseAuditReadAuthorization Forbidden { get; } =
        new(CourseAuditReadStatus.Forbidden, Guid.Empty);

    public static CourseAuditReadAuthorization Allowed(Guid actorId) =>
        actorId == Guid.Empty
            ? throw new ArgumentException("An allowed read needs an account.", nameof(actorId))
            : new(CourseAuditReadStatus.Allowed, actorId);

    public bool IsAllowed => Status == CourseAuditReadStatus.Allowed;
}

/// <summary>
/// AUD-06, S-14: authorizes <c>audit.events.read</c> on <c>course/{courseId}</c> in the application
/// layer, right before the course query. The route's Access metadata already ran the same decision in
/// <c>AccessControlMiddleware</c>; this second check keeps the query service from ever being called
/// without a current Allow (ICourseAuditQueryService does no authorization itself), and makes a
/// revocation between the two checks take effect in the same request.
/// The course comes only from the route; the subject only from the server-side subject provider.
/// Stateless and singleton-safe: <see cref="IAccessDecision"/> is a singleton in Platform.Access.
/// </summary>
internal sealed class CourseAuditReadAuthorizer(IAccessDecision decision)
{
    private readonly IAccessDecision _decision = decision ?? throw new ArgumentNullException(nameof(decision));

    public async ValueTask<CourseAuditReadAuthorization> AuthorizeAsync(
        SubjectRef? subject,
        Guid courseId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // The middleware has already denied (and audited) an anonymous caller; no second decision here.
        if (subject is null)
            return CourseAuditReadAuthorization.Unauthenticated;

        if (courseId == Guid.Empty || string.IsNullOrWhiteSpace(correlationId))
            return CourseAuditReadAuthorization.Forbidden;

        AccessDecision? result;
        try
        {
            result = await _decision.DecideAsync(
                new AccessRequest(
                    subject,
                    AccessActions.AuditEventsRead.Id,
                    new ResourceRef(AccessActions.AuditEventsRead.ResourceType!, courseId.ToString("D")),
                    correlationId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A failed decision never grants access, and its text never reaches the caller.
            return CourseAuditReadAuthorization.Forbidden;
        }

        if (result is null || result.Outcome != AccessOutcome.Allow)
        {
            return result?.DenialReason == AccessDenialReason.Unauthenticated
                ? CourseAuditReadAuthorization.Unauthenticated
                : CourseAuditReadAuthorization.Forbidden;
        }

        // CourseAuditReadPolicy already requires this. It is checked again because the viewer is
        // recorded as a User actor: a non-account subject is never turned into one.
        return subject.State == AccountState.Active
               && Guid.TryParse(subject.Id, out var accountId)
               && accountId != Guid.Empty
            ? CourseAuditReadAuthorization.Allowed(accountId)
            : CourseAuditReadAuthorization.Forbidden;
    }
}
