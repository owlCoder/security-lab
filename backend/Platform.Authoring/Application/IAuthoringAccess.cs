namespace Platform.Authoring.Application;

public enum AuthoringAction
{
    CreateDraft = 1,
    EditDraft = 2,
    CreateDraftFromTemplate = 3,
}

/// <summary>
/// Authoring-side view of the platform's IAccessDecision contract (specification 3.1):
/// subject + action + concrete resource. The decision is made on the server and the
/// implementation must deny by default (ACC-05): an unknown actor, action or resource is denied.
/// For <see cref="AuthoringAction.EditDraft"/> the resource is the draft id, and a draft that
/// does not exist or belongs to someone else is denied the same way, so callers cannot probe for ids.
/// </summary>
public interface IAuthoringAccess
{
    bool IsAllowed(Guid actorId, AuthoringAction action, Guid? resourceId);
}
