using Platform.Assignments.Domain.Models;
using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Domain;
 
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
// Podrazumevano odbijanje: sve sto nije jasno (student, revizija) znaci "zakljucano".
public sealed class AssignmentPrerequisiteGate : IAssignmentPrerequisiteGate
{
    private readonly IRevisionLaboratoryResolver _resolver;
    private readonly IPrerequisiteAccessService _access;
 
    public AssignmentPrerequisiteGate(
        IRevisionLaboratoryResolver resolver,
        IPrerequisiteAccessService access)
    {
        _resolver = resolver;
        _access = access;
    }
 
    public async ValueTask<PrerequisiteCheckResult> CheckAsync(
        string studentId,
        Assignment assignment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
 
        // PrerequisiteAccessService radi sa Guid studentom; StartAttemptService sa stringom.
        if (!Guid.TryParse(studentId, out Guid studentGuid) || studentGuid == Guid.Empty)
        {
            return PrerequisiteCheckResult.Denied(PrerequisiteReasonCodes.InvalidStudent);
        }
 
        Guid? laboratoryId = await _resolver.ResolveLaboratoryIdAsync(
            assignment.PublishedLabRevisionId,
            cancellationToken);
 
        if (laboratoryId is null || laboratoryId.Value == Guid.Empty)
        {
            return PrerequisiteCheckResult.Denied(PrerequisiteReasonCodes.LaboratoryUnresolved);
        }
 
        PrerequisiteAccessResult access = await _access.CheckAccessAsync(
            studentGuid,
            laboratoryId.Value,
            cancellationToken);
 
        return access.IsUnlocked
            ? PrerequisiteCheckResult.Unlocked
            : PrerequisiteCheckResult.Locked(access.MissingPrerequisites);
    }
}