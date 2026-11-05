namespace Platform.LabLocalization.Domain;

/// <summary>
/// The text of one laboratory revision in one language (LAB-07).
/// It deliberately has no evaluator: grading belongs to the revision, so every language
/// variant of a revision is graded the same way and a translation cannot change it.
/// Immutable and always valid: the only way to obtain an instance is <see cref="Create"/>,
/// and the setters are private, so a translation cannot be moved to another revision afterwards.
/// </summary>
public sealed record LabLocalization
{
    public const int MaxTitleLength = 120;
    public const int MaxDescriptionLength = 2000;
    public const int MaxInstructionsLength = 20000;

    public Guid RevisionId { get; private init; }
    public LabLanguage Language { get; private init; }
    public string Title { get; private init; } = "";
    public string Description { get; private init; } = "";
    public string Instructions { get; private init; } = "";

    private LabLocalization() { }

    /// <summary>
    /// Text is stored as written (no HTML cleaning); clients must render it as text, not as HTML.
    /// </summary>
    /// <exception cref="DomainRuleException">
    /// INVALID_REVISION_ID, INVALID_LANGUAGE, INVALID_TITLE, INVALID_DESCRIPTION or INVALID_INSTRUCTIONS.
    /// </exception>
    public static LabLocalization Create(
        Guid revisionId, LabLanguage language, string? title, string? description, string? instructions)
    {
        if (revisionId == Guid.Empty)
            throw new DomainRuleException(LocalizationErrorCodes.InvalidRevisionId);

        if (!Enum.IsDefined(language))
            throw new DomainRuleException(LocalizationErrorCodes.InvalidLanguage);

        var cleanTitle = title?.Trim() ?? "";
        if (cleanTitle.Length == 0 || cleanTitle.Length > MaxTitleLength || cleanTitle.Any(char.IsControl))
            throw new DomainRuleException(LocalizationErrorCodes.InvalidTitle);

        var cleanDescription = CleanMultiline(description, MaxDescriptionLength, LocalizationErrorCodes.InvalidDescription);
        var cleanInstructions = CleanMultiline(instructions, MaxInstructionsLength, LocalizationErrorCodes.InvalidInstructions);

        return new LabLocalization
        {
            RevisionId = revisionId,
            Language = language,
            Title = cleanTitle,
            Description = cleanDescription,
            Instructions = cleanInstructions
        };
    }

    /// <summary>Line breaks and tabs are allowed; other control characters (e.g. \0) are not.</summary>
    private static string CleanMultiline(string? text, int maxLength, string errorCode)
    {
        var clean = text?.Trim() ?? "";
        if (clean.Length > maxLength || clean.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new DomainRuleException(errorCode);

        return clean;
    }
}
