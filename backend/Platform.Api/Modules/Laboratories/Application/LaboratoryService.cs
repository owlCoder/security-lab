using Platform.Api.Modules.Laboratories.Domain;
using Platform.LabVisibility.Contracts;

namespace Platform.Api.Modules.Laboratories.Application;

public enum LabResultStatus
{
    Ok,
    InvalidTitle,
    InvalidCourse,
    Forbidden,
    NotFound,
    Unauthenticated
}

public sealed record LabResult(LabResultStatus Status, Laboratory? Laboratory = null)
{
    public static LabResult Ok(Laboratory laboratory) => new(LabResultStatus.Ok, laboratory);
    public static readonly LabResult InvalidTitle = new(LabResultStatus.InvalidTitle);
    public static readonly LabResult InvalidCourse = new(LabResultStatus.InvalidCourse);
    public static readonly LabResult Forbidden = new(LabResultStatus.Forbidden);
    public static readonly LabResult NotFound = new(LabResultStatus.NotFound);
    public static readonly LabResult Unauthenticated = new(LabResultStatus.Unauthenticated);
}

// LAB-01 slucajevi upotrebe: kreiranje, citanje i preimenovanje laboratorije.
//
// Poziva se tek posle AccessCheckFilter-a (uloga i poznata radnja). Ovde se dodatno proverava
// vlasnistvo nad konkretnom laboratorijom: nastavnik vidi i menja samo laboratorije koje je
// sam kreirao. Tudja laboratorija se prikazuje kao nepostojeca (404), da odgovor ne otkrije
// da postoji. Siri pristup (kurs, saradnici) dolazi sa LAB-03 / ACC integracijom.
//
// LAB-03: laboratorija se pravi samo na kursu na kome je pozivalac nastavnik (ICourseMembership).
public sealed class LaboratoryService(
    ILaboratoryRepository repository,
    ICourseMembership courseMembership,
    TimeProvider timeProvider,
    ILogger<LaboratoryService> logger)
{
    public async Task<LabResult> CreateAsync(
        Guid? courseId, string? title, IIdentityContext caller, CancellationToken cancellationToken)
    {
        // Autor dolazi iskljucivo iz serverskog identiteta, nikad iz tela zahteva.
        Guid? creatorId = CallerId.From(caller);
        if (creatorId is null)
        {
            return LabResult.Unauthenticated;
        }

        // Laboratorija ne postoji van kursa.
        if (courseId is null || courseId == Guid.Empty)
        {
            return LabResult.InvalidCourse;
        }

        // Kurs bira klijent, zato server proverava ulogu bas na tom kursu.
        // Provera je pre provere naslova: ko nije nastavnik ne dobija ni povratnu informaciju o naslovu.
        CourseRole role = await courseMembership.GetRoleAsync(courseId.Value, creatorId.Value, cancellationToken);
        LabCourseDecision decision = LabCoursePolicy.DecideCreate(role);
        if (!decision.Allowed)
        {
            // Razlog ide samo u serverski log; klijent dobija isti odgovor za nepostojeci i tudji kurs.
            logger.LogWarning(
                "Laboratory create denied: action={Action} courseId={CourseId} by={CallerId} reason={Reason}",
                LabActions.Create, courseId.Value, creatorId.Value, decision.Code);
            return LabResult.Forbidden;
        }

        if (!IsAcceptableTitle(title))
        {
            return LabResult.InvalidTitle;
        }

        Laboratory laboratory;
        try
        {
            // Domen generise novi Id i vreme kreiranja na serveru.
            laboratory = new Laboratory(courseId.Value, title!, creatorId.Value, timeProvider);
        }
        catch (ArgumentException)
        {
            return LabResult.InvalidTitle;
        }

        await repository.AddAsync(laboratory, cancellationToken);

        logger.LogInformation(
            "Laboratory created: action={Action} laboratoryId={LaboratoryId} courseId={CourseId} createdBy={CreatedBy} reason={Reason}",
            LabActions.Create, laboratory.Id, laboratory.CourseId, laboratory.CreatedBy, decision.Code);

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
        if (laboratory is null)
        {
            return LabResult.NotFound;
        }

        // LAB-03: kurs se uzima iz sacuvane laboratorije; za laboratoriju bez kursa clanstvo se ne pita.
        CourseRole role = laboratory.CourseId == Guid.Empty
            ? CourseRole.None
            : await courseMembership.GetRoleAsync(laboratory.CourseId, callerId.Value, cancellationToken);

        LabOpenDecision decision = LabOpenPolicy.DecideOpen(
            laboratory.CourseId, role, isCreator: laboratory.CreatedBy == callerId.Value);
        if (!decision.Allowed)
        {
            // Isti NotFound kao za nepostojecu laboratoriju; razlog ide samo u serverski log.
            logger.LogWarning(
                "Laboratory open denied: action={Action} laboratoryId={LaboratoryId} courseId={CourseId} by={CallerId} reason={Reason}",
                LabActions.Read, laboratory.Id, laboratory.CourseId, callerId.Value, decision.Code);
            return LabResult.NotFound;
        }

        return LabResult.Ok(laboratory);
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
        if (!LaboratoryVisibility.IsVisibleTo(laboratory, callerId.Value))
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

    // Brza provera pre domena; konacno pravilo (obavezan, najvise 200 znakova) je u Laboratory.
    private static bool IsAcceptableTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title) && title.Trim().Length <= Laboratory.MaxTitleLength;
}