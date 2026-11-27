namespace Platform.Competencies.Domain;

public sealed class Competency
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 500;

    public Guid Id { get; }
    public string Name { get; }
    public string? Description { get; }

    public Competency(Guid id, string name, string? description = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Competency ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Name must contain 1–{MaxNameLength} characters.", nameof(name));
        if (description?.Trim().Length > MaxDescriptionLength)
            throw new ArgumentException($"Description must not exceed {MaxDescriptionLength} characters.", nameof(description));

        Id = id;
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }
}
