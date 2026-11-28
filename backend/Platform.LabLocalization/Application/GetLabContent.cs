namespace Platform.LabLocalization.Application;

using Platform.LabLocalization.Domain;

/// <summary>
/// A laboratory's text in one language. <see cref="Language"/> is the language actually
/// returned, which is the default one when the requested translation does not exist.
/// </summary>
public sealed record LabContentView(
    Guid RevisionId,
    string EvaluatorVersion,
    LabLanguage Language,
    string Title,
    string Description,
    string Instructions);

/// <summary>
/// Reads a laboratory in the requested language (LAB-07). The revision and its evaluator are
/// chosen before the language is applied, so every language shows the same revision and is
/// graded by the same evaluator: only the text differs.
/// The default-language (Serbian) text is the revision's own text; other languages come from
/// stored translations and fall back to it.
/// It does not check who is asking: the caller must authorise the read first.
/// </summary>
public sealed class GetLabContent(ILabRevisionReader revisions, ILabLocalizationRepository localizations)
{
    /// <exception cref="DomainRuleException">INVALID_LANGUAGE or NOT_FOUND.</exception>
    public async Task<LabContentView> ExecuteAsync(Guid labId, string? requestedLanguage, CancellationToken ct = default)
    {
        if (!LabLanguages.TryParse(requestedLanguage, out var language))
            throw new DomainRuleException(LocalizationErrorCodes.InvalidLanguage);

        var revision = await revisions.FindCurrentForLabAsync(labId, ct)
            ?? throw new DomainRuleException(LocalizationErrorCodes.NotFound);

        if (language != LabLanguages.Default &&
            await localizations.GetAsync(revision.RevisionId, language, ct) is { } translation)
        {
            return new LabContentView(
                revision.RevisionId,
                revision.EvaluatorVersion,
                translation.Language,
                translation.Title,
                translation.Description,
                translation.Instructions);
        }

        return new LabContentView(
            revision.RevisionId,
            revision.EvaluatorVersion,
            LabLanguages.Default,
            revision.Text.Title,
            revision.Text.Description,
            revision.Text.Instructions);
    }
}
