namespace Platform.Api.Modules.Laboratories.Domain;

public sealed class Laboratory
{
    public const int MaxTitleLength = 200;

    public Guid Id { get; }
    public string Title { get; private set; }
    public LabStatus Status { get; }
    public Guid CreatedBy { get; }
    public DateTimeOffset CreatedAt { get; }

    public Laboratory(string title, Guid createdBy, TimeProvider? timeProvider = null)
        : this(Guid.NewGuid(), title, LabStatus.Draft, createdBy,
            (timeProvider ?? TimeProvider.System).GetUtcNow())
    {
    }

    private Laboratory(Guid id, string title, LabStatus status, Guid createdBy, DateTimeOffset createdAt)
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
        Title = ValidateTitle(title);
        Status = status;
        CreatedBy = createdBy;
        CreatedAt = createdAt.ToUniversalTime();
    }

    // For trusted persistence adapters; incoming API data must not select these values.
    public static Laboratory Rehydrate(
        Guid id, string title, LabStatus status, Guid createdBy, DateTimeOffset createdAt) =>
        new(id, title, status, createdBy, createdAt);

    public void Rename(string title)
    {
        Title = ValidateTitle(title);
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
