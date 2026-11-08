namespace Platform.AssignmentOverview.Prerequisites;
 
// ASN-07 TIM 16
// Format kao reasonCode u Audit katalogu: ^[a-z][a-z0-9_]{0,31}$
public static class PrerequisiteReasonCodes
{
    public const string NotMet = "prerequisite_not_met";
    public const string InvalidStudent = "invalid_student";
    public const string LaboratoryUnresolved = "laboratory_unresolved";
}
 
// ASN-07 TIM 16
// Ishod provere preduslova za jednu dodelu. Ovo koristi i prikaz "zakljucano".
// Sadrzi samo ID-jeve laboratorija koje fale, nikakve skrivene kriterijume.
public sealed record PrerequisiteCheckResult(
    bool IsUnlocked,
    IReadOnlyList<Guid> MissingLaboratoryIds,
    string? ReasonCode)
{
    public static PrerequisiteCheckResult Unlocked { get; } =
        new(true, Array.Empty<Guid>(), null);
 
    public static PrerequisiteCheckResult Locked(IReadOnlyList<Guid> missing) =>
        new(false, missing, PrerequisiteReasonCodes.NotMet);
 
    // Provera nije mogla da se uradi (nevalidan student, nepoznata revizija). Uvek zakljucano.
    public static PrerequisiteCheckResult Denied(string reasonCode) =>
        new(false, Array.Empty<Guid>(), reasonCode);
}