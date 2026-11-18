using Platform.Competencies.Domain;

namespace Platform.Api.Modules.Competencies;

public enum CompetencyOperationStatus
{
    Ok, Created, NoContent, InvalidLaboratoryId, InvalidCompetencyId,
    LaboratoryNotFound, CompetencyNotFound, StorageUnavailable
}

public sealed record CompetencyOperationResult(
    CompetencyOperationStatus Status,
    IReadOnlyList<Competency>? Competencies = null,
    string? LaboratoryTitle = null,
    bool CanManage = false);

