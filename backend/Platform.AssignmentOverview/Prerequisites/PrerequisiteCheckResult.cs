namespace Platform.AssignmentOverview.Prerequisites;
 
// ASN-07 TIM 16
// Format kao reasonCode u Audit katalogu: ^[a-z][a-z0-9_]{0,31}$
public static class PrerequisiteReasonCodes
{
    public const string NotMet = "prerequisite_not_met";
    public const string InvalidStudent = "invalid_student";
}
 
// ASN-07 TIM 16
// Ishod provere preduslova za jednu dodelu. Ovo koristi i prikaz "zakljucano":
// isLocked = !IsUnlocked, missingAssignmentIds = MissingAssignmentIds.
// Sadrzi samo ID-jeve zadataka koji fale, nikakve skrivene kriterijume.
public sealed record PrerequisiteCheckResult(
    bool IsUnlocked,
    IReadOnlyList<Guid> MissingAssignmentIds,
    string? ReasonCode)
{
    public static PrerequisiteCheckResult Unlocked { get; } =
        new(true, Array.Empty<Guid>(), null);
 
    public static PrerequisiteCheckResult Locked(IReadOnlyList<Guid> missing) =>
        new(false, missing, PrerequisiteReasonCodes.NotMet);
 
    // Provera nije mogla da se uradi (npr. nevalidan student). Uvek zakljucano.
    public static PrerequisiteCheckResult Denied(string reasonCode) =>
        new(false, Array.Empty<Guid>(), reasonCode);
}