
using Platform.LabMetadata;

namespace Platform.Api.Modules.Laboratories.Api;

public sealed record LaboratoryMetadataRequest(
    string? Objective,
    LaboratoryDifficulty Difficulty,
    LaboratoryType Type,
    List<string>? Tags
);


public sealed record LaboratoryMetadataResponse(
    Guid LaboratoryId,
    string Objective,
    LaboratoryDifficulty Difficulty,
    LaboratoryType Type,
    IReadOnlyList<string> Tags
);
