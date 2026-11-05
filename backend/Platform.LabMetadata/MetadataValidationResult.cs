
using System.Collections.Generic;
using System.Linq;

namespace Platform.LabMetadata;

public sealed class MetadataValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public MetadataValidationResult(
        IEnumerable<MetadataValidationError> errors)
    {
        Errors = errors
            .GroupBy(error => error.Field)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(error => error.Message)
                    .ToArray()
            );
    }
}
