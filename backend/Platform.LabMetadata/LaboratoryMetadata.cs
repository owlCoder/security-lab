
using System;
using System.Collections.Generic;
using System.Linq;

namespace Platform.LabMetadata
{
    public class LaboratoryMetadata
    {
        // Referenca na postojecu laboratoriju iz LAB-01.
        // LAB-02 ne generise novi identitet.
        public Guid LaboratoryId { get; }

        public string Objective { get; private set; }
        public LaboratoryDifficulty Difficulty { get; private set; }
        public LaboratoryType Type { get; private set; }
        public IReadOnlyList<string> Tags { get; private set; }

        public LaboratoryMetadata(
            Guid laboratoryId,
            string objective,
            LaboratoryDifficulty difficulty,
            LaboratoryType type,
            IEnumerable<string> tags)
        {
            if (laboratoryId == Guid.Empty)
                throw new ArgumentException(
                    "Laboratory ID cannot be empty.",
                    nameof(laboratoryId));

            if (string.IsNullOrWhiteSpace(objective))
                throw new ArgumentException(
                    "Laboratory objective is required.",
                    nameof(objective));


            if (objective.Trim().Length >
                LaboratoryMetadataValidator.MaxObjectiveLength)
            {
                throw new ArgumentException(
                    $"Laboratory objective cannot exceed " +
                    $"{LaboratoryMetadataValidator.MaxObjectiveLength} characters.",
                    nameof(objective));
            }


            if (!Enum.IsDefined(difficulty))
                throw new ArgumentOutOfRangeException(nameof(difficulty));

            if (!Enum.IsDefined(type))
                throw new ArgumentOutOfRangeException(nameof(type));

            if (tags is null)
                throw new ArgumentNullException(nameof(tags));

            LaboratoryId = laboratoryId;
            Objective = objective.Trim();
            Difficulty = difficulty;
            Type = type;

            Tags = Array.AsReadOnly(
                tags
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToArray()
            );
        }
    }
}
