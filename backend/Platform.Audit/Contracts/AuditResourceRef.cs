namespace Platform.Audit.Contracts;

/// <param name="Type">One of <see cref="AuditResourceTypes"/>.</param>
/// <param name="Id">At most 64 characters: ASCII letters, digits, '-' and '_'.</param>
public sealed record AuditResourceRef(string Type, string Id);

public static class AuditResourceTypes
{
    public const string Account = "Account";
    public const string Course = "Course";
    public const string Delegation = "Delegation";
    public const string Revision = "Revision";
}
