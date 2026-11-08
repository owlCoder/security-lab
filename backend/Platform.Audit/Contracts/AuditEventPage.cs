namespace Platform.Audit.Contracts;

/// <summary>Newest first. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record AuditEventPage(IReadOnlyList<AuditEventView> Items, string? NextCursor);

public enum AuditReadStatus
{
    /// <summary>The page is returned and the view itself is recorded as audit.events.viewed.</summary>
    Ok,

    /// <summary>A filter, limit or cursor is invalid. Nothing was read or recorded.</summary>
    InvalidQuery,

    /// <summary>Storage failed or the view could not be recorded. No data is returned.</summary>
    Unavailable
}

public sealed record AuditReadResult(AuditReadStatus Status, AuditEventPage? Page)
{
    public static AuditReadResult Ok(AuditEventPage page) => new(AuditReadStatus.Ok, page);

    public static AuditReadResult InvalidQuery() => new(AuditReadStatus.InvalidQuery, null);

    public static AuditReadResult Unavailable() => new(AuditReadStatus.Unavailable, null);
}
