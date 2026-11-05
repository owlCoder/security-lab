namespace Platform.Api.Modules.Laboratories.Domain;

// Minimalna revizija laboratorije (LAB-01): pokazuje da ID laboratorije
// ostaje isti kroz revizije. Objava sa snapshot-om definicije je REV-01.
public sealed class LabRevision
{
    public Guid Id { get; }
    public Guid LaboratoryId { get; }
    public int Number { get; }

    public LabRevision(Guid laboratoryId, int number) : this(Guid.NewGuid(), laboratoryId, number)
    {
    }

    // Rekonstrukcija postojece revizije na serveru (npr. iz baze).
    public LabRevision(Guid id, Guid laboratoryId, int number)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Revision ID is required.", nameof(id));
        }

        if (laboratoryId == Guid.Empty)
        {
            throw new ArgumentException("Laboratory ID is required.", nameof(laboratoryId));
        }

        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Revision number starts at 1.");
        }

        Id = id;
        LaboratoryId = laboratoryId;
        Number = number;
    }
}
