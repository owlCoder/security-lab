using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Domain;
using Platform.Assignments.Domain.Models;
 
namespace Platform.AssignmentOverview.Prerequisites;
 
// ASN-07 TIM 16
public interface IAssignmentPrerequisiteGate
{
    ValueTask<PrerequisiteCheckResult> CheckAsync(
        string studentId,
        Assignment assignment,
        CancellationToken cancellationToken = default);
}
 
// ASN-07 TIM 16
// Server odlucuje da li je dodela otkljucana za studenta:
//   1. ucita preduslove dodele (IAssignmentPrerequisiteStore),
//   2. ucita ishode pokusaja studenta za zahtevane zadatke (IAttemptOutcomeReader),
//   3. CompletionPolicy.FindMissing vrati sta fali.
// Podrazumevano odbijanje: nevalidan student je zakljucano, a bez ishoda pokusaja preduslov nije ispunjen.
public sealed class AssignmentPrerequisiteGate : IAssignmentPrerequisiteGate
{
    private readonly IAssignmentPrerequisiteStore _store;
    private readonly IAttemptOutcomeReader _outcomes;
 
    public AssignmentPrerequisiteGate(
        IAssignmentPrerequisiteStore store,
        IAttemptOutcomeReader outcomes)
    {
        _store = store;
        _outcomes = outcomes;
    }
 
    public async ValueTask<PrerequisiteCheckResult> CheckAsync(
        string studentId,
        Assignment assignment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
 
        // CompletionPolicy baca izuzetak za nevalidan ID, pa ga odbijamo unapred.
        if (!Identifiers.IsValidPrincipalId(studentId))
        {
            return PrerequisiteCheckResult.Denied(PrerequisiteReasonCodes.InvalidStudent);
        }
 
        IReadOnlyList<AssignmentPrerequisite> prerequisites =
            await _store.GetForAssignmentAsync(assignment.Id, cancellationToken);
 
        if (prerequisites.Count == 0)
        {
            return PrerequisiteCheckResult.Unlocked;
        }
 
        List<Guid> requiredIds = prerequisites
            .Select(p => p.RequiredAssignmentId)
            .Distinct()
            .ToList();
 
        IReadOnlyList<AttemptOutcomeSnapshot> attempts =
            await _outcomes.GetForStudentAsync(studentId, requiredIds, cancellationToken);
 
        IReadOnlyList<Guid> missing = CompletionPolicy.FindMissing(studentId, prerequisites, attempts);
 
        return missing.Count == 0
            ? PrerequisiteCheckResult.Unlocked
            : PrerequisiteCheckResult.Locked(missing);
    }
}