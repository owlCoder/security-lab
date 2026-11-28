namespace Team21.Acc.Infrastructure.Groups.Fakes;

/// <summary>
/// Fixed ids of the Development demo data. Readable on purpose, used as X-Test-User values.
/// </summary>
public static class DemoIds
{
    // Same ids as the ACC-01 sample courses OIB-2026 (active) and OIB-2025 (archived)
    public static readonly Guid Course = Guid.Parse("21000000-0000-0000-0000-000000000001");
    public static readonly Guid ArchivedCourse = Guid.Parse("21000000-0000-0000-0000-000000000003");

    public static readonly Guid Teacher = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid Assistant = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid StudentA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid StudentB = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    public static readonly Guid BlockedStudent = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    public static readonly Guid Outsider = Guid.Parse("00000000-0000-0000-0000-0000000000ff");

    public static readonly Guid GroupA = Guid.Parse("00000000-0000-0000-0000-00000000a00a");
    public static readonly Guid GroupB = Guid.Parse("00000000-0000-0000-0000-00000000a00b");

    // Assignment ids, as the ASN module would own them
    public static readonly Guid RestrictedToA = Guid.Parse("00000000-0000-0000-0000-00000000d00a");
    public static readonly Guid CourseWide = Guid.Parse("00000000-0000-0000-0000-00000000d0cc");
    public static readonly Guid InArchivedCourse = Guid.Parse("00000000-0000-0000-0000-00000000d0a0");
}
