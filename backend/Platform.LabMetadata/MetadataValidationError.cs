
namespace Platform.LabMetadata;

public sealed record MetadataValidationError(
    string Field,
    string Message
);
