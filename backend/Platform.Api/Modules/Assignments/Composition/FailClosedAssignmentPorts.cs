using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;

namespace Platform.Api.Modules.Assignments.Composition;

// ASN-04 / ASN-06 TIM 15 (celina 5)
//
// Podrazumevane implementacije portova koje pišu drugi članovi tima (1: skladišta, 4: autorizacija).
// Registruju se sa TryAdd, pa ih prava implementacija zamenjuje bez izmene ovog fajla.
// Ne implementiraju tuđu logiku: samo obezbeđuju da host može da se podigne (ValidateOnBuild)
// i da se, dok pravi adapter ne postoji, sve odbija (ACC-05: podrazumevano odbijanje).

/// <summary>
/// Oznaka za podrazumevanu (nepovezanu) implementaciju porta. <see cref="AssignmentsCompositionCheck"/>
/// po njoj pri pokretanju prijavljuje koji portovi još nisu povezani.
/// </summary>
public interface IFailClosedAssignmentPort
{
}

/// <summary>Port nije povezan sa pravom implementacijom; API vraća 503 bez detalja.</summary>
public sealed class AssignmentPortNotConfiguredException(string port)
    : Exception($"Assignments port {port} is not configured.");

/// <summary>Nijedna dodela nije poznata: start vraća 404, produženje 403.</summary>
public sealed class NotConfiguredAssignmentRepository : IAssignmentRepository, IFailClosedAssignmentPort
{
    public ValueTask<Assignment?> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<Assignment?>(null);

    public ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
        IReadOnlyCollection<string> courseIds, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<Assignment>>([]);

    public ValueTask AddAsync(Assignment assignment, CancellationToken cancellationToken = default) =>
        throw new AssignmentPortNotConfiguredException(nameof(IAssignmentRepository));

    public ValueTask UpdateAsync(Assignment assignment, CancellationToken cancellationToken = default) =>
        throw new AssignmentPortNotConfiguredException(nameof(IAssignmentRepository));
}

public sealed class NotConfiguredAttemptStore : IAttemptStore, IFailClosedAssignmentPort
{
    public ValueTask<Guid?> TryStartAsync(
        Guid assignmentId, string studentId, string publishedLabRevisionId, int maxAttempts, Guid requestId,
        CancellationToken cancellationToken = default) =>
        throw new AssignmentPortNotConfiguredException(nameof(IAttemptStore));
}

public sealed class NotConfiguredDeadlineExtensionStore : IDeadlineExtensionStore, IFailClosedAssignmentPort
{
    public ValueTask<StudentDeadlineOverride?> GetAsync(
        Guid assignmentId, string studentId, CancellationToken cancellationToken = default) =>
        throw new AssignmentPortNotConfiguredException(nameof(IDeadlineExtensionStore));

    public ValueTask SaveAsync(
        StudentDeadlineOverride deadlineOverride, DeadlineExtensionAuditEntry auditEntry,
        CancellationToken cancellationToken = default) =>
        throw new AssignmentPortNotConfiguredException(nameof(IDeadlineExtensionStore));
}

public sealed class DenyAllStartAttemptAuthorization : IStartAttemptAuthorization, IFailClosedAssignmentPort
{
    public ValueTask<bool> CanStartAsync(
        string studentId, Guid assignmentId, string courseId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}

public sealed class DenyAllDeadlineExtensionAuthorization : IDeadlineExtensionAuthorization, IFailClosedAssignmentPort
{
    public ValueTask<bool> CanExtendAsync(
        string actorId, Guid assignmentId, string courseId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}

public sealed class NoActiveEnrollmentCheck : ICourseEnrollmentCheck, IFailClosedAssignmentPort
{
    public ValueTask<bool> IsActiveStudentAsync(
        string studentId, string courseId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}
