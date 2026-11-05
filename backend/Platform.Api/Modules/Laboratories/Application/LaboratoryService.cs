using Platform.Api.Modules.Laboratories.Domain;

namespace Platform.Api.Modules.Laboratories.Application;

public enum LabResultStatus
{
    Ok,
    InvalidTitle,
    NotFound,
    Unauthenticated
}

public sealed record LabResult(LabResultStatus Status, Laboratory? Laboratory = null)
{
    public static LabResult Ok(Laboratory laboratory) => new(LabResultStatus.Ok, laboratory);
    public static readonly LabResult InvalidTitle = new(LabResultStatus.InvalidTitle);
    public static readonly LabResult NotFound = new(LabResultStatus.NotFound);
    public static readonly LabResult Unauthenticated = new(LabResultStatus.Unauthenticated);
}

// LAB-01 slucajevi upotrebe: kreiranje, citanje i preimenovanje laboratorije.
//
// Poziva se tek posle AccessCheckFilter-a (uloga i poznata radnja). Ovde se dodatno proverava
// vlasnistvo nad konkretnom laboratorijom: nastavnik vidi i menja samo laboratorije koje je
// sam kreirao. Tudja laboratorija se prikazuje kao nepostojeca (404), da odgovor ne otkrije
// da postoji. Siri pristup (kurs, saradnici) dolazi sa LAB-03 / ACC integracijom.
public sealed class LaboratoryService(
    ILaboratoryRepository repository,
    TimeProvider timeProvider,
    ILogger<LaboratoryService> logger)
{
    public async Task<LabResult> CreateAsync(
        string? title, IIdentityContext caller, CancellationToken cancellationToken)
    {
        // Autor dolazi iskljucivo iz serverskog identiteta, nikad iz tela zahteva.
        Guid? creatorId = CallerId.From(caller);
        if (creatorId is null)
        {
            return LabResult.Unauthenticated;
        }

        if (!IsAcceptableTitle(title))
        {
            return LabResult.InvalidTitle;
        }

        Laboratory laboratory;
        try
        {
            // Domen generise novi Id i vreme kreiranja na serveru.
            laboratory = new Laboratory(title!, creatorId.Value, timeProvider);
        }
        catch (ArgumentException)
        {
            return LabResult.InvalidTitle;
        }

        await repository.AddAsync(laboratory, cancellationToken);

        logger.LogInformation(
            "Laboratory created: laboratoryId={LaboratoryId} createdBy={CreatedBy}",
            laboratory.Id, laboratory.CreatedBy);

        return LabResult.Ok(laboratory);
    }

    public async Task<LabResult> GetAsync(
        Guid id, IIdentityContext caller, CancellationToken cancellationToken)
    {
        Guid? callerId = CallerId.From(caller);
        if (callerId is null)
        {
            return LabResult.Unauthenticated;
        }

        Laboratory? laboratory = await repository.GetByIdAsync(id, cancellationToken);
        return IsVisibleTo(laboratory, callerId.Value)
            ? LabResult.Ok(laboratory!)
            : LabResult.NotFound;
    }

    public async Task<LabResult> RenameAsync(
        Guid id, string? title, IIdentityContext caller, CancellationToken cancellationToken)
    {
        Guid? callerId = CallerId.From(caller);
        if (callerId is null)
        {
            return LabResult.Unauthenticated;
        }

        Laboratory? laboratory = await repository.GetByIdAsync(id, cancellationToken);
        if (!IsVisibleTo(laboratory, callerId.Value))
        {
            return LabResult.NotFound;
        }

        if (!IsAcceptableTitle(title))
        {
            return LabResult.InvalidTitle;
        }

        try
        {
            // Rename menja samo naslov; Id, autor, status i vreme kreiranja ostaju isti.
            laboratory!.Rename(title!);
        }
        catch (ArgumentException)
        {
            return LabResult.InvalidTitle;
        }

        try
        {
            await repository.UpdateAsync(laboratory, cancellationToken);
        }
        catch (LaboratoryNotFoundException)
        {
            // Obrisana izmedju citanja i upisa.
            return LabResult.NotFound;
        }

        logger.LogInformation(
            "Laboratory renamed: laboratoryId={LaboratoryId} by={CallerId}",
            laboratory.Id, callerId.Value);

        return LabResult.Ok(laboratory);
    }

    private static bool IsVisibleTo(Laboratory? laboratory, Guid callerId) =>
        laboratory is not null && laboratory.CreatedBy == callerId;

    // Brza provera pre domena; konacno pravilo (obavezan, najvise 200 znakova) je u Laboratory.
    private static bool IsAcceptableTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title) && title.Trim().Length <= Laboratory.MaxTitleLength;
}