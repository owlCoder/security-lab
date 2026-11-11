using Platform.LabMetadata;

namespace Platform.Api.Modules.Laboratories.Application;

public sealed record LabSearchFilters(
    LaboratoryDifficulty? Difficulty = null,
    LaboratoryType? Type = null,
    IReadOnlyList<string>? Tags = null)
{
    public bool HasValues => Difficulty is not null || Type is not null || Tags is { Count: > 0 };

    public bool TryNormalize(out LabSearchFilters normalized)
    {
        normalized = this;
        if ((Difficulty is not null && !Enum.IsDefined(Difficulty.Value))
            || (Type is not null && !Enum.IsDefined(Type.Value))
            || (Tags?.Any(tag => string.IsNullOrWhiteSpace(tag)
                || tag.Any(char.IsControl)) ?? false))
        {
            return false;
        }

        normalized = this with
        {
            Tags = Tags?.Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
        };
        return true;
    }

    public bool Matches(LaboratoryMetadata metadata) =>
        (Difficulty is null || metadata.Difficulty == Difficulty)
        && (Type is null || metadata.Type == Type)
        && (Tags?.All(tag => metadata.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) ?? true);
}

public sealed record LabSearchFilterOptions(
    LabSearchStatus Status,
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Tags,
    bool MetadataAvailable);
