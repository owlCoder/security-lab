namespace Platform.LabLocalization.Application;

using Platform.LabLocalization.Domain;

/// <summary>
/// Adds a translation to a laboratory revision (LAB-07). It authorises first, on the server,
/// before touching any data, and it only ever stores text: the revision and its evaluator
/// are not changed. An existing translation is never overwritten.
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
    /// <exception cref="DomainRuleException">FORBIDDEN, INVALID_LANGUAGE, NOT_FOUND, CONFLICT or a validation code from the domain.</exception>
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
        if (!access.IsAllowed(actorId, LocalizationAction.AddTranslation, revisionId))
            throw new DomainRuleException(LocalizationErrorCodes.Forbidden);

        if (string.IsNullOrWhiteSpace(language) || !LabLanguages.TryParse(language, out var parsedLanguage))
            throw new DomainRuleException(LocalizationErrorCodes.InvalidLanguage);

        var revision = await revisions.FindForLabAsync(labId, revisionId, ct)
            ?? throw new DomainRuleException(LocalizationErrorCodes.NotFound);

        var localization = LabLocalization.Create(revision.RevisionId, parsedLanguage, title, description, instructions);
        await localizations.AddAsync(localization, ct);
        return localization;
    }
}
