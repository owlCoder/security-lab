namespace Platform.Audit.Contracts;

/// <summary>
/// Course audit view for teachers (AUD-06).
/// <para>
/// PRECONDITION: the caller has ALREADY authorized <c>audit.events.read</c> on this course for
/// the viewer (Platform.Access). This reader does not decide access; it only guarantees that the
/// returned events belong to <c>courseId</c>, and it records the view (<c>audit.events.viewed</c>)
/// BEFORE any data is returned. If that record cannot be stored the result is
/// <see cref="AuditReadStatus.Unavailable"/> and no data leaves the module (fail closed).
/// </para>
/// <para>
/// <paramref name="viewer"/> must be built by the host from a server-validated session and the
/// server correlation ID, never from client input.
/// </para>
/// </summary>
public interface IAuditCourseReader
{
    ValueTask<AuditReadResult> ReadCourseEventsAsync(
        Guid courseId,
        AuditContext viewer,
        AuditEventQuery query,
        CancellationToken cancellationToken = default);
}
