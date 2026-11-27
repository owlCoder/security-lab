using Platform.Api.Modules.Access;

namespace Platform.Api.Modules.Administration;

[Flags]
public enum AdminCapability { None = 0, ViewAdministration = 1, ManageSettings = 2, ManageDelegations = 4 }
public enum GrantState { Active, Revoked }
public sealed record CourseSnapshot(Guid Id, string DisplayName, int Version);
public sealed record AdminGrant(Guid Id, Guid CourseId, Guid GranteeId, Guid GrantedBy,
    AdminCapability Capabilities, GrantState State, int Version, bool IsRoot = false);
public sealed record AdministrationView(Guid CourseId, string DisplayName, int Version, string[] Capabilities);
public sealed record GrantView(Guid Id, Guid CourseId, Guid GranteeId, string[] Capabilities, string State, int Version);
public sealed record RenameCourseRequest(string? DisplayName, int ExpectedVersion);
public sealed record DelegateRequest(Guid GranteeId, string[]? Capabilities);
public sealed record RevokeRequest(int ExpectedVersion);
public sealed record AdminWriteResult(WriteStatus Status, object? Value = null, int? Before = null, int? After = null);

// Administrative data lives here. Membership, teaching assignments, answers and grades do not.
public interface ICourseAdministrationStore
{
    PortResult<CourseSnapshot> FindCourse(Guid course) => throw AsyncRequired();
    PortResult<AdminGrant> FindAuthority(Guid actor, Guid course) => throw AsyncRequired();
    PortResult<AdministrationView> ReadAdministration(AdminGrant authority) => throw AsyncRequired();
    AdminWriteResult Rename(AdminGrant authority, RenameCourseRequest request) => throw AsyncRequired();
    AdminWriteResult Delegate(AdminGrant authority, Guid grantee, AdminCapability capabilities) => throw AsyncRequired();
    AdminWriteResult Revoke(AdminGrant authority, Guid grantId, int expectedVersion) => throw AsyncRequired();
    ValueTask<PortResult<CourseSnapshot>> FindCourseAsync(Guid course, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(FindCourse(course)); }
    ValueTask<PortResult<AdminGrant>> FindAuthorityAsync(Guid actor, Guid course, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(FindAuthority(actor, course)); }
    ValueTask<PortResult<AdministrationView>> ReadAdministrationAsync(AdminGrant authority, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(ReadAdministration(authority)); }
    ValueTask<AdminWriteResult> RenameAsync(AdminGrant authority, RenameCourseRequest request, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(Rename(authority, request)); }
    ValueTask<AdminWriteResult> DelegateAsync(AdminGrant authority, Guid grantee, AdminCapability capabilities, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(Delegate(authority, grantee, capabilities)); }
    ValueTask<AdminWriteResult> RevokeAsync(AdminGrant authority, Guid grantId, int expectedVersion, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(Revoke(authority, grantId, expectedVersion)); }
    private static NotSupportedException AsyncRequired() => new("Use the asynchronous administration port for this adapter.");
}
