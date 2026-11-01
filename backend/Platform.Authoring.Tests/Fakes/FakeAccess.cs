using Platform.Authoring.Application;

namespace Platform.Authoring.Tests.Fakes;

/// <summary>
/// Deny-by-default policy: only listed authors may author, and only the owner may edit an existing draft.
/// A missing draft is denied exactly like someone else's draft.
/// </summary>
public sealed class FakeAuthoringAccess(InMemoryDraftRepository drafts, params Guid[] authors) : IAuthoringAccess
{
    public bool IsAllowed(Guid actorId, AuthoringAction action, Guid? resourceId)
    {
        if (actorId == Guid.Empty || !authors.Contains(actorId)) return false;

        return action switch
        {
            AuthoringAction.CreateDraft => true,
            AuthoringAction.CreateDraftFromTemplate => true,
            AuthoringAction.EditDraft => resourceId is { } id && drafts.Peek(id)?.OwnerId == actorId,
            _ => false,
        };
    }
}

/// <summary>Grants everything. Used only to reach service paths that sit behind the access check.</summary>
public sealed class AllowAllAccess : IAuthoringAccess
{
    public bool IsAllowed(Guid actorId, AuthoringAction action, Guid? resourceId) => true;
}
