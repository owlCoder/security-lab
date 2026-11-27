namespace Platform.LabLocalization.Application;

using Platform.LabLocalization.Domain;

/// <summary>
/// Adds a translation to a laboratory revision (LAB-07). It authorises first, on the server,
/// before touching any data, and it only ever stores text: the revision and its evaluator
/// are not changed. An existing translation is never overwritten.
/// Only a draft accepts translations, so published text cannot change without a new reviewed revision.
/// The default language is the revision's own text and is never stored as a translation.
/// </summary>
public sealed class AddLabLocalization(
    ILocalizationAccess access,
    ILabRevisionReader revisions,
    ILabLocalizationRepository localizations)
{
    /// <summary>
    /// The language is required here: unlike a read, a missing value must not silently write the default language.
    /// A revision the actor may not translate gives FORBIDDEN whether or not it exists, so callers cannot probe for ids.
    /// </summary>
    /// <exception cref="DomainRuleException">FORBIDDEN, INVALID_LANGUAGE, NOT_FOUND, REVISION_NOT_DRAFT, CONFLICT or a validation code from the domain.</exception>
    public async Task<LabLocalization> ExecuteAsync(
        Guid actorId,
        Guid labId,
        Guid revisionId,
        string? language,
        string? title,
        string? description,
        string? instructions,
        CancellationToken ct = default)
    {
        if (!await access.IsAllowedAsync(actorId, LocalizationAction.AddTranslation, revisionId, ct))
            throw new DomainRuleException(LocalizationErrorCodes.Forbidden);

        if (string.IsNullOrWhiteSpace(language) || !LabLanguages.TryParse(language, out var parsedLanguage) ||
            parsedLanguage == LabLanguages.Default)
            throw new DomainRuleException(LocalizationErrorCodes.InvalidLanguage);

        var revision = await revisions.FindForLabAsync(labId, revisionId, ct)
            ?? throw new DomainRuleException(LocalizationErrorCodes.NotFound);

        if (!revision.IsDraft)
            throw new DomainRuleException(LocalizationErrorCodes.RevisionNotDraft);

        var localization = LabLocalization.Create(revision.RevisionId, parsedLanguage, title, description, instructions);
        await localizations.AddAsync(localization, ct);
        return localization;
    }
}
