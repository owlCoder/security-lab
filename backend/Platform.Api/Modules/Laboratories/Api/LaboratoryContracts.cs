namespace Platform.Api.Modules.Laboratories.Api;

// Javni HTTP ugovor LAB-01.
//
// Zahtev namerno ima SAMO naslov i kurs. Polja koja klijent eventualno posalje (id, createdBy,
// status, createdAt...) ne postoje u ovom tipu, pa se pri deserijalizaciji ignorisu:
// klijent ne moze da izabere ID, autora ni status (mass assignment zastita).
// LAB-03: kurs bira klijent, ali server proverava da je pozivalac nastavnik bas na tom kursu.
public sealed record CreateLaboratoryRequest(string? Title, Guid? CourseId);

// Preimenovanje nema kurs: kurs laboratorije se ne menja posle kreiranja.
public sealed record RenameLaboratoryRequest(string? Title);

public sealed record LaboratoryResponse(
    Guid Id,
    Guid CourseId,
    string Title,
    string Status,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record LaboratorySearchItem(
    Guid Id,
    string Title,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record LaboratorySearchResponse(
    IReadOnlyList<LaboratorySearchItem> Items,
    bool Truncated);

public sealed record LaboratorySearchFiltersResponse(
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Tags,
    bool MetadataAvailable);

// Kodovi su stabilni; frontend (api/labs.ts) ih prevodi u poruke za korisnika.
public sealed record ApiErrorResponse(string Code, string Message);

public static class LabErrorCodes
{
    public const string InvalidTitle = "INVALID_TITLE";
    public const string InvalidCourse = "INVALID_COURSE";
    public const string InvalidBody = "INVALID_BODY";
    public const string NotFound = "NOT_FOUND";
    public const string InvalidQuery = "INVALID_QUERY";
    public const string FiltersUnavailable = "FILTERS_UNAVAILABLE";
}
