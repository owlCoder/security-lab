using Platform.Assignments.Application;
using Platform.Assignments.Contracts;
 
namespace Platform.AssignmentOverview.Prerequisites;
 
// ASN-07 TIM 16
// Omotac oko StartAttemptService (tim 15). Redosled provera:
//   1. dodela postoji, 2. student ima pravo da pokrene (autorizacija),
//   3. preduslovi su ispunjeni, 4. tek onda poziv tudjeg servisa.
// Autorizacija ide PRE preduslova, da student koji nema pristup ne sazna sta je zakljucano.
public sealed class GuardedStartAttemptService
{
    private readonly IAssignmentRepository _assignments;
    private readonly IStartAttemptAuthorization _authorization;
    private readonly IAssignmentPrerequisiteGate _gate;
    private readonly StartAttemptService _inner;
 
    public GuardedStartAttemptService(
        IAssignmentRepository assignments,
        IStartAttemptAuthorization authorization,
        IAssignmentPrerequisiteGate gate,
        StartAttemptService inner)
    {
        _assignments = assignments;
        _authorization = authorization;
        _gate = gate;
        _inner = inner;
    }
 
    public async ValueTask<Guid> StartAttemptAsync(
        Guid assignmentId,
        string studentId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _assignments.GetByIdAsync(assignmentId, cancellationToken);
 
        if (assignment == null)
        {
            throw new KeyNotFoundException("Assignment not found.");
        }
 
        if (string.IsNullOrWhiteSpace(studentId))
        {
            throw new UnauthorizedAccessException("Access denied.");
        }
 
        if (!await _authorization.CanStartAsync(
            studentId,
            assignment.Id,
            assignment.CourseId,
            cancellationToken))
        {
            throw new UnauthorizedAccessException("Access denied.");
        }
 
        var check = await _gate.CheckAsync(studentId, assignment, cancellationToken);
 
        if (!check.IsUnlocked)
        {
            throw new PrerequisitesNotMetException(
                check.ReasonCode ?? PrerequisiteReasonCodes.NotMet,
                check.MissingLaboratoryIds);
        }
 
        return await _inner.StartAttemptAsync(
            assignmentId,
            studentId,
            requestId,
            cancellationToken);
    }
}