namespace Platform.LaboratoryPersistence.Infrastructure.Persistence.Entities;

public sealed class LabRevisionRecord
{
    public Guid Id { get; set; }
    public Guid LaboratoryId { get; set; }
    public int Number { get; set; }
    public LaboratoryRecord Laboratory { get; set; } = null!;
}
