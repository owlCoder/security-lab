namespace Platform.Authoring.Domain;

/// <summary>
/// A read-only starting point for a new task (AUT-06). Fully immutable: copying a template
/// produces an independent draft and cannot modify the source template.
/// </summary>
public sealed class LabTemplate
{
    public const int MaxNameLength = 80;

    public Guid Id { get; }
    public string Name { get; }
    public LabContent Content { get; }

    private LabTemplate(Guid id, string name, LabContent content)
    {
        Id = id;
        Name = name;
        Content = content;
    }

    /// <exception cref="DomainRuleException">INVALID_ID or INVALID_NAME.</exception>
    public static LabTemplate Create(Guid id, string? name, LabContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (id == Guid.Empty) throw new DomainRuleException(AuthoringErrorCodes.InvalidId);

        var cleanName = name?.Trim() ?? "";
        if (cleanName.Length == 0 || cleanName.Length > MaxNameLength || cleanName.Any(char.IsControl))
            throw new DomainRuleException(AuthoringErrorCodes.InvalidName);

        return new LabTemplate(id, cleanName, content);
    }

    /// <summary>
    /// Creates a new draft from this template. The draft gets its own id; an identifier is never
    /// reused, so the template id is rejected as a draft id.
    /// </summary>
    /// <exception cref="DomainRuleException">INVALID_ID.</exception>
    public LabDraft CreateDraft(Guid newDraftId, Guid ownerId)
    {
        if (newDraftId == Id) throw new DomainRuleException(AuthoringErrorCodes.InvalidId);
        return LabDraft.Create(newDraftId, ownerId, Content, sourceTemplateId: Id);
    }
}
