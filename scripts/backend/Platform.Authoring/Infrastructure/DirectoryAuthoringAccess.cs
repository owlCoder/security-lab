using Platform.Authoring.Application;

namespace Platform.Authoring.Infrastructure;

/// <summary>
/// The access check used inside the use cases (defence in depth; the API has already made the
/// platform-wide decision through Platform.Access). Least privilege: only the template actions are
/// allowed, and only for authors. Creating a blank draft and editing a draft stay denied until the
/// draft endpoints exist and bring their own ownership check (AUT-01).
/// </summary>
public sealed class DirectoryAuthoringAccess(IAuthorDirectory authors) : IAuthoringAccess
{
    public bool IsAllowed(Guid actorId, AuthoringAction action, Guid? resourceId)
    {
        if (actorId == Guid.Empty) return false;

        return action switch
        {
            AuthoringAction.ListTemplates => authors.IsAuthor(actorId),
            AuthoringAction.CreateDraftFromTemplate => authors.IsAuthor(actorId),
            _ => false,
        };
    }
}
