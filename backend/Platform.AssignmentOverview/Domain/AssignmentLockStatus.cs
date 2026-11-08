namespace Platform.AssignmentOverview.Domain;

// ASN-07: prikaz „zaključano“
// Rezultat za jednu dodelu: da li je zaključana i šta student prvo mora da uradi.
// MissingAssignmentIds sadrži samo dodele koje student već vidi u svojoj listi.
public sealed record AssignmentLockStatus(
    bool IsLocked,
    IReadOnlyList<Guid> MissingAssignmentIds)
{
    public static AssignmentLockStatus Unlocked { get; } =
        new(false, Array.Empty<Guid>());

    public static AssignmentLockStatus Locked(IReadOnlyList<Guid> visibleMissingAssignmentIds) =>
        new(true, visibleMissingAssignmentIds);
}