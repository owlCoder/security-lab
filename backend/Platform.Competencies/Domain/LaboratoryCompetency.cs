namespace Platform.Competencies.Domain;

// LaboratoryId is a reference to LAB-01's stable identity, not a new laboratory or revision.
public sealed class LaboratoryCompetency
{
    public Guid LaboratoryId { get; }
    public Guid CompetencyId { get; }

    public LaboratoryCompetency(Guid laboratoryId, Guid competencyId)
    {
        if (laboratoryId == Guid.Empty) throw new ArgumentException("Laboratory ID is required.", nameof(laboratoryId));
        if (competencyId == Guid.Empty) throw new ArgumentException("Competency ID is required.", nameof(competencyId));
        LaboratoryId = laboratoryId;
        CompetencyId = competencyId;
    }
}
