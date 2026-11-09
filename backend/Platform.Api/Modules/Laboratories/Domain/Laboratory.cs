namespace Platform.Api.Modules.Laboratories.Domain;

public sealed class Laboratory
{
    public const int MaxTitleLength = 200;

    public Guid Id { get; }
    // LAB-03: kurs kome laboratorija pripada. Ne menja se posle kreiranja.
    public Guid CourseId { get; }
    public string Title { get; private set; }
    public LabStatus Status { get; }
    public Guid CreatedBy { get; }
    public DateTimeOffset CreatedAt { get; }

    // Naslov je namerno izmedju dva Guid-a, da se kurs i autor ne zamene slucajno.
    public Laboratory(Guid courseId, string title, Guid createdBy, TimeProvider? timeProvider = null)
        : this(Guid.NewGuid(), RequireCourse(courseId), title, LabStatus.Draft, createdBy,
            (timeProvider ?? TimeProvider.System).GetUtcNow())
    {
    }

    private Laboratory(Guid id, Guid courseId, string title, LabStatus status, Guid createdBy, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Laboratory ID is required.", nameof(id));
        }

        if (createdBy == Guid.Empty)
        {
            throw new ArgumentException("Laboratory creator is required.", nameof(createdBy));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (createdAt == default)
        {
            throw new ArgumentException("Laboratory creation time is required.", nameof(createdAt));
        }

        Id = id;
        CourseId = courseId;
        Title = ValidateTitle(title);
        Status = status;
        CreatedBy = createdBy;
        CreatedAt = createdAt.ToUniversalTime();
    }

    // For trusted persistence adapters; incoming API data must not select these values.
    public static Laboratory Rehydrate(
        Guid id, Guid courseId, string title, LabStatus status, Guid createdBy, DateTimeOffset createdAt) =>
        new(id, RequireCourse(courseId), title, status, createdBy, createdAt);

    // PRIVREMENO (LAB-03): skladiste Tima 20 jos nema kolonu CourseId (vidi docs/components/LAB-03.md).
    // Ovako ucitana laboratorija nema kurs (Guid.Empty), pa je provera pri otvaranju odbija.
    public static Laboratory Rehydrate(
        Guid id, string title, LabStatus status, Guid createdBy, DateTimeOffset createdAt) =>
        new(id, Guid.Empty, title, status, createdBy, createdAt);

    public void Rename(string title)
    {
        Title = ValidateTitle(title);
    }

    // Laboratorija ne postoji van kursa.
    private static Guid RequireCourse(Guid courseId)
    {
        if (courseId == Guid.Empty)
        {
            throw new ArgumentException("Laboratory course is required.", nameof(courseId));
        }

        return courseId;
    }

    private static string ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Laboratory title is required.", nameof(title));
        }

        string normalized = title.Trim();
        if (normalized.Length > MaxTitleLength)
        {
            throw new ArgumentException($"Laboratory title must not exceed {MaxTitleLength} characters.", nameof(title));
        }

        return normalized;
    }
}
