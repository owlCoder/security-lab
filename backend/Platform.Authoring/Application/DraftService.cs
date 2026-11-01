using Platform.Authoring.Domain;

namespace Platform.Authoring.Application;

/// <summary>
/// Use cases for AUT-01 (create and edit drafts) and AUT-06 (create a draft from a template).
/// Every use case authorises first, on the server, before touching any data.
/// </summary>
public sealed class DraftService(
    IDraftRepository drafts,
    ITemplateRepository templates,
    IAuthoringAccess access,
    Func<Guid>? newId = null)
{
    private readonly Func<Guid> _newId = newId ?? (() => Guid.NewGuid());

    /// <exception cref="DomainRuleException">FORBIDDEN, or a validation code from the domain.</exception>
    public async Task<LabDraft> CreateDraftAsync(Guid actorId, LabContent content, CancellationToken ct = default)
    {
        Authorize(actorId, AuthoringAction.CreateDraft, null);

        var draft = LabDraft.Create(_newId(), actorId, content);
        await drafts.AddAsync(draft, ct);
        return draft;
    }

    /// <summary>
    /// Edits a draft. <paramref name="expectedVersion"/> is the version the caller last saw.
    /// A draft the actor may not edit gives FORBIDDEN whether or not it exists.
    /// </summary>
    /// <exception cref="DomainRuleException">FORBIDDEN, NOT_FOUND, CONFLICT or DRAFT_NOT_EDITABLE.</exception>
    public async Task<LabDraft> EditDraftAsync(Guid actorId, Guid draftId, int expectedVersion, LabContent content, CancellationToken ct = default)
    {
        Authorize(actorId, AuthoringAction.EditDraft, draftId);

        var draft = await drafts.GetAsync(draftId, ct) ?? throw new DomainRuleException(AuthoringErrorCodes.NotFound);
        var previousVersion = draft.Version;
        if (draft.Edit(content, expectedVersion))
            await drafts.SaveAsync(draft, previousVersion, ct);
        return draft;
    }

    /// <summary>Creates an independent draft from a template. The template is never modified.</summary>
    /// <exception cref="DomainRuleException">FORBIDDEN or NOT_FOUND.</exception>
    public async Task<LabDraft> CreateDraftFromTemplateAsync(Guid actorId, Guid templateId, CancellationToken ct = default)
    {
        Authorize(actorId, AuthoringAction.CreateDraftFromTemplate, templateId);

        var template = await templates.GetAsync(templateId, ct) ?? throw new DomainRuleException(AuthoringErrorCodes.NotFound);
        var draft = template.CreateDraft(_newId(), actorId);
        await drafts.AddAsync(draft, ct);
        return draft;
    }

    private void Authorize(Guid actorId, AuthoringAction action, Guid? resourceId)
    {
        if (actorId == Guid.Empty || !access.IsAllowed(actorId, action, resourceId))
            throw new DomainRuleException(AuthoringErrorCodes.Forbidden);
    }
}
