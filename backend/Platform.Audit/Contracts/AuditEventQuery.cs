namespace Platform.Audit.Contracts;

/// <summary>Filters of the course audit view. Every field is optional.</summary>
/// <param name="EventType">A type from <see cref="AuditEventCatalog"/>.</param>
/// <param name="From">Inclusive lower bound, UTC.</param>
/// <param name="To">Inclusive upper bound, UTC.</param>
/// <param name="Limit">1 to <see cref="AuditReadLimits.MaxLimit"/>; default <see cref="AuditReadLimits.DefaultLimit"/>.</param>
/// <param name="Cursor">Value of <see cref="AuditEventPage.NextCursor"/> from the previous page.</param>
public sealed record AuditEventQuery(
    string? EventType = null,
    AuditOutcome? Outcome = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int? Limit = null,
    string? Cursor = null);

public static class AuditReadLimits
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
}
