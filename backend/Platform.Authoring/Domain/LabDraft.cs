namespace Platform.Authoring.Domain;

/// <summary>
/// A laboratory working copy (AUT-01). Attempts never point at a draft: they are bound to an
/// immutable published revision (REV-03), so editing a draft cannot affect an attempt.
/// </summary>
public sealed class LabDraft
{
    public Guid Id { get; private init; }
    public Guid OwnerId { get; private init; }

    /// <summary>Set when the draft was copied from a template (AUT-06); the template itself is never touched.</summary>
    public Guid? SourceTemplateId { get; private init; }

    public DraftStatus Status { get; private set; }
    public LabContent Content { get; private set; } = null!;

    /// <summary>Starts at 1 and grows on every real change; used to detect concurrent edits.</summary>
    public int Version { get; private set; }

    private LabDraft() { }

    /// <exception cref="DomainRuleException">INVALID_ID for an empty draft, owner or source-template id.</exception>
    public static LabDraft Create(Guid id, Guid ownerId, LabContent content, Guid? sourceTemplateId = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        RequireId(id);
        RequireId(ownerId);
        if (sourceTemplateId == Guid.Empty) throw new DomainRuleException(AuthoringErrorCodes.InvalidId);

        return new LabDraft
        {
            Id = id,
            OwnerId = ownerId,
            SourceTemplateId = sourceTemplateId,
            Status = DraftStatus.Draft,
            Content = content,
            Version = 1,
        };
    }

    /// <summary>Rebuilds a stored draft. Intended for persistence adapters and tests; applies the same checks as <see cref="Create"/>.</summary>
    /// <exception cref="DomainRuleException">INVALID_ID or INVALID_VERSION.</exception>
    public static LabDraft Rehydrate(Guid id, Guid ownerId, Guid? sourceTemplateId, DraftStatus status, LabContent content, int version)
    {
        var draft = Create(id, ownerId, content, sourceTemplateId);
        if (!Enum.IsDefined(status) || version < 1) throw new DomainRuleException(AuthoringErrorCodes.InvalidVersion);
        draft.Status = status;
        draft.Version = version;
        return draft;
    }

    /// <summary>Replaces the content. Returns false (and changes nothing) when the content is identical.</summary>
    /// <exception cref="DomainRuleException">DRAFT_NOT_EDITABLE when not in Draft; CONFLICT when <paramref name="expectedVersion"/> is stale.</exception>
    public bool Edit(LabContent content, int expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Status != DraftStatus.Draft) throw new DomainRuleException(AuthoringErrorCodes.DraftNotEditable);
        if (expectedVersion != Version) throw new DomainRuleException(AuthoringErrorCodes.Conflict);
        if (content == Content) return false;

        Content = content;
        Version++;
        return true;
    }

    private static void RequireId(Guid id)
    {
        if (id == Guid.Empty) throw new DomainRuleException(AuthoringErrorCodes.InvalidId);
    }
}
