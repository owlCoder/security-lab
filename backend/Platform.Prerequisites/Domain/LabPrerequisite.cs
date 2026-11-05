namespace Platform.Prerequisites.Domain;

public sealed class LabPrerequisite
{
    public Guid LaboratoryId { get; }
    public Guid RequiredLaboratoryId { get; }

    public LabPrerequisite(Guid laboratoryId, Guid requiredLaboratoryId)
    {
        if (laboratoryId == Guid.Empty || requiredLaboratoryId == Guid.Empty)
        {
            throw new ArgumentException("ID laboratorije ne sme biti prazan.");
        }

        if (laboratoryId == requiredLaboratoryId)
        {
            throw new ArgumentException("Laboratorija ne može biti sopstveni preduslov.");
        }

        LaboratoryId = laboratoryId;
        RequiredLaboratoryId = requiredLaboratoryId;
    }
}