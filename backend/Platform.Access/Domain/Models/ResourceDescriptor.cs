namespace Platform.Access.Domain.Models;

public sealed record ResourceDescriptor(
    string Type,
    string Id,
    Guid? CourseId,
    Guid? OwnerId,
    string? Status,
    IReadOnlyDictionary<string, AttributeValue> Attributes);
