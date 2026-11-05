
using System;
using System.Collections.Generic;

namespace Platform.LabMetadata;

public sealed class LaboratoryMetadataValidator
{
    // Maksimalna dozvoljena duzina cilja laboratorije.
    public const int MaxObjectiveLength = 2000;

    public MetadataValidationResult Validate(
        Guid laboratoryId,
        string? objective,
        LaboratoryDifficulty difficulty,
        LaboratoryType type,
        IEnumerable<string>? tags)
    {
        var errors = new List<MetadataValidationError>();

        if (laboratoryId == Guid.Empty)
        {
            errors.Add(new(
                "laboratoryId",
                "Laboratory ID is required."
            ));
        }

        if (string.IsNullOrWhiteSpace(objective))
        {
            errors.Add(new(
                "objective",
                "Laboratory objective is required."
            ));
        }
        else if (objective.Trim().Length > MaxObjectiveLength)
        {
            errors.Add(new(
                "objective",
                $"Objective cannot exceed {MaxObjectiveLength} characters."
            ));
        }

        if (!Enum.IsDefined(difficulty))
        {
            errors.Add(new(
                "difficulty",
                "Invalid laboratory difficulty."
            ));
        }

        if (!Enum.IsDefined(type))
        {
            errors.Add(new(
                "type",
                "Invalid laboratory type."
            ));
        }

        if (tags is null)
        {
            errors.Add(new(
                "tags",
                "Laboratory tags collection is required."
            ));
        }

        return new MetadataValidationResult(errors);
    }
}
