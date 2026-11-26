namespace Platform.Api.Modules.LabLocalization;

public sealed record AddTranslationRequest(string? Language, string? Title, string? Description, string? Instructions);

// Language je jezik koji je stvarno vraćen: srpski kada traženi prevod ne postoji.
public sealed record LabContentResponse(
    Guid RevisionId,
    string EvaluatorVersion,
    string Language,
    string Title,
    string Description,
    string Instructions);

public sealed record LabTranslationResponse(
    Guid RevisionId,
    string Language,
    string Title,
    string Description,
    string Instructions);
