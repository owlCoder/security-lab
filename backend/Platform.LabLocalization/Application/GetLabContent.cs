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

        var localization = await localizations.GetAsync(revision.RevisionId, language, ct)
            ?? await localizations.GetAsync(revision.RevisionId, LabLanguages.Default, ct)
            ?? throw new DomainRuleException(LocalizationErrorCodes.NotFound);

        return new LabContentView(
            revision.RevisionId,
            revision.EvaluatorVersion,
            localization.Language,
            localization.Title,
            localization.Description,
            localization.Instructions);
    }
}
