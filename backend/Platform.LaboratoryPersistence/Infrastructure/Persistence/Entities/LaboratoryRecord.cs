namespace Platform.LaboratoryPersistence.Infrastructure.Persistence.Entities;

// Database row only. Domain models live in Platform.Api (LAB-01) and Platform.LabMetadata (LAB-02).
public sealed class LaboratoryRecord
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Goal { get; set; }
    public string? Difficulty { get; set; }
    public string? Type { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<LabPrerequisiteRecord> Prerequisites { get; set; } = new();
    public List<LabRevisionRecord> Revisions { get; set; } = new();
}
