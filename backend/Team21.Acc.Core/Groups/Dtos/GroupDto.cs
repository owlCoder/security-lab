namespace Team21.Acc.Core.Groups.Dtos;

/// <summary>A group as the api returns it. Status is "Active" or "Archived".</summary>
public sealed record GroupDto(
    Guid Id,
    Guid CourseId,
    string Name,
    string Status);
