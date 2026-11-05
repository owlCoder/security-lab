
namespace Platform.LaboratoryPersistence.Infrastructure.Persistence.Entities;

public sealed class LabPrerequisiteRecord
{
    public Guid LaboratoryId { get; set; }

    public Guid PrerequisiteLabId { get; set; }

    public LaboratoryRecord Laboratory { get; set; } = null!;

    public LaboratoryRecord PrerequisiteLab { get; set; } = null!;
}
