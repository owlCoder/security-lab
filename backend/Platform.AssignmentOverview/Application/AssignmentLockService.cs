using Platform.AssignmentOverview.Domain;
using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Domain;

namespace Platform.AssignmentOverview.Application;

// ASN-07: prikaz „zaključano“
// Za svaku dodelu iz studentove liste računa da li je zaključana.
public sealed class AssignmentLockService
{
    private readonly IAssignmentPrerequisiteStore _prerequisites;
    private readonly IAttemptOutcomeReader _attempts;

    public AssignmentLockService(
        IAssignmentPrerequisiteStore prerequisites,
        IAttemptOutcomeReader attempts)
    {
        _prerequisites = prerequisites;
        _attempts = attempts;
    }

    public async ValueTask<IReadOnlyDictionary<Guid, AssignmentLockStatus>> GetLockStatusesAsync(
        string studentId,
        IReadOnlyList<StudentAssignmentView> assignments,
        CancellationToken cancellationToken = default)
    {
        // 1. Student mora biti poznat; ID dolazi iz sesije, nikad od klijenta.
        if (string.IsNullOrWhiteSpace(studentId))
        {
            throw new ArgumentException("Student ID is required.", nameof(studentId));
        }

        ArgumentNullException.ThrowIfNull(assignments);

        // 2. Dodele koje student već vidi u svojoj listi (ASN-02).
        var visibleAssignmentIds = assignments.Select(a => a.AssignmentId).ToHashSet();

        // 3. Preduslovi svake dodele (Jovanova tabela).
        var prerequisitesByAssignment = new Dictionary<Guid, IReadOnlyList<AssignmentPrerequisite>>();
        var requiredAssignmentIds = new HashSet<Guid>();

        foreach (StudentAssignmentView assignment in assignments)
        {
            IReadOnlyList<AssignmentPrerequisite> prerequisites =
                await _prerequisites.GetForAssignmentAsync(assignment.AssignmentId, cancellationToken);

            // Odbrana u dubinu: uzimamo samo veze koje stvarno pripadaju ovoj dodeli.
            List<AssignmentPrerequisite> own = prerequisites
                .Where(p => p.AssignmentId == assignment.AssignmentId)
                .ToList();

            prerequisitesByAssignment[assignment.AssignmentId] = own;

            foreach (AssignmentPrerequisite prerequisite in own)
            {
                requiredAssignmentIds.Add(prerequisite.RequiredAssignmentId);
            }
        }

        // 4. Pokušaji ovog studenta, samo za dodele koje su nekome preduslov.
        IReadOnlyList<AttemptOutcomeSnapshot> attempts = requiredAssignmentIds.Count == 0
            ? Array.Empty<AttemptOutcomeSnapshot>()
            : await _attempts.GetForStudentAsync(studentId, requiredAssignmentIds, cancellationToken);

        // 5. Za svaku dodelu: šta još fali (Jovanovo pravilo „urađeno“).
        var result = new Dictionary<Guid, AssignmentLockStatus>();

        foreach (StudentAssignmentView assignment in assignments)
        {
            IReadOnlyList<Guid> missing = CompletionPolicy.FindMissing(
                studentId,
                prerequisitesByAssignment[assignment.AssignmentId],
                attempts);

            if (missing.Count == 0)
            {
                result[assignment.AssignmentId] = AssignmentLockStatus.Unlocked;
                continue;
            }

            // 6. Dodela je zaključana. Studentu pokazujemo samo preduslove koje već vidi,
            //    da ne otkrijemo dodele drugih grupa ili kurseva (ASN-10).
            List<Guid> visibleMissing = missing
                .Where(visibleAssignmentIds.Contains)
                .ToList();

            result[assignment.AssignmentId] = AssignmentLockStatus.Locked(visibleMissing);
        }

        return result;
    }
}