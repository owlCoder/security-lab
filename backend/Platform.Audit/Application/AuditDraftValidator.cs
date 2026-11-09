using System.Text.Json;
using System.Text.RegularExpressions;
using Platform.Audit.Contracts;

namespace Platform.Audit.Application;

/// <summary>
/// Checks a draft and its trusted context against <see cref="AuditEventCatalog"/>.
/// Stateless. Returns <see cref="AuditRejectionCode.None"/> when the event may be stored.
/// A rejected value is never copied into the result, a log or an exception.
/// </summary>
internal sealed partial class AuditDraftValidator
{
    // Additional check only; the allowlist in the catalog is the primary control.
    private static readonly string[] ForbiddenKeyParts =
        ["password", "passwd", "secret", "token", "authorization", "apikey", "cookie", "session"];

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceIdPattern();

    public AuditRejectionCode Validate(AuditEventDraft draft, AuditContext context)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(context);

        if (!AuditEventCatalog.TryGet(draft.EventType, out var schema))
            return AuditRejectionCode.UnknownEventType;

        // An undefined enum value is not a key either, so it is rejected here as well.
        if (!schema.ActorsByOutcome.TryGetValue(draft.Outcome, out var allowedActors))
            return AuditRejectionCode.OutcomeNotAllowed;

        if (!IsValidContext(context, allowedActors))
            return AuditRejectionCode.InvalidContext;

        var courseResult = CheckCourse(schema.Course, draft.CourseId);
        if (courseResult != AuditRejectionCode.None)
            return courseResult;

        if (!IsValidResource(schema, draft.Resource))
            return AuditRejectionCode.InvalidResource;

        if (schema.RequiredDetails is not null && schema.RequiredDetails.Any(key =>
                draft.Details is null || !draft.Details.ContainsKey(key)))
            return AuditRejectionCode.InvalidDetailValue;

        return CheckDetails(schema, draft.Outcome, draft.Details);
    }

    private static bool IsValidContext(AuditContext context, IReadOnlySet<AuditActorKind> allowedActors)
    {
        var actor = context.Actor;
        if (actor is null || !allowedActors.Contains(actor.Kind))
            return false;

        var hasId = actor.Id is Guid id && id != Guid.Empty;
        var idMatchesKind = actor.Kind == AuditActorKind.Anonymous ? actor.Id is null : hasId;

        return idMatchesKind
            && context.CorrelationId is not null
            && CorrelationIdPattern().IsMatch(context.CorrelationId);
    }

    // An empty GUID is never a valid course.
    private static AuditRejectionCode CheckCourse(AuditPresence rule, Guid? courseId) => rule switch
    {
        AuditPresence.Required when courseId is null || courseId == Guid.Empty => AuditRejectionCode.CourseRequired,
        AuditPresence.Optional when courseId == Guid.Empty => AuditRejectionCode.CourseRequired,
        AuditPresence.Forbidden when courseId is not null => AuditRejectionCode.CourseNotAllowed,
        _ => AuditRejectionCode.None
    };

    private static bool IsValidResource(AuditEventSchema schema, AuditResourceRef? resource)
    {
        if (resource is null)
            return schema.Resource != AuditPresence.Required;

        if (schema.Resource == AuditPresence.Forbidden)
            return false;

        var typeAllowed = schema.ResourceType is null
            ? resource.Type is AuditResourceTypes.Account or AuditResourceTypes.Course or AuditResourceTypes.Delegation or AuditResourceTypes.Revision
            : string.Equals(resource.Type, schema.ResourceType, StringComparison.Ordinal);

        return typeAllowed
            && resource.Id is not null
            && ResourceIdPattern().IsMatch(resource.Id);
    }

    private static AuditRejectionCode CheckDetails(
        AuditEventSchema schema,
        AuditOutcome outcome,
        IReadOnlyDictionary<string, string>? details)
    {
        if (details is null || details.Count == 0)
            return AuditRejectionCode.None;

        // Size limits first, so an oversized input is refused before any per-key work.
        if (details.Count > AuditEventCatalog.MaxDetailKeys
            || JsonSerializer.SerializeToUtf8Bytes(details).Length > AuditEventCatalog.MaxDetailsBytes)
            return AuditRejectionCode.DetailsTooLarge;

        foreach (var (key, value) in details)
        {
            if (IsForbiddenKeyName(key))
                return AuditRejectionCode.ForbiddenDetailKey;

            if (!schema.Details.TryGetValue(key, out var field)
                || (field.OnlyForOutcomes is not null && !field.OnlyForOutcomes.Contains(outcome)))
                return AuditRejectionCode.UnknownDetailKey;

            if (!AuditDetailFormats.IsValid(field.Format, value))
                return AuditRejectionCode.InvalidDetailValue;
        }

        return AuditRejectionCode.None;
    }

    private static bool IsForbiddenKeyName(string key) =>
        ForbiddenKeyParts.Any(part => key.Contains(part, StringComparison.OrdinalIgnoreCase));
}
