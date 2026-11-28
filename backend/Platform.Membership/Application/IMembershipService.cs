using Platform.Access.Domain.Models;
namespace Platform.Membership.Application;

public interface IMembershipService
{
    /// <summary>
    /// ACC-02: upis studenta u kurs. Pozivalac (caller) dolazi sa servera, nikad iz tela zahteva.
    /// </summary>
    ValueTask<EnrollmentResult> EnrollStudentAsync(
    SubjectRef? caller,
    Guid courseId,
    Guid studentAccountId,
    string correlationId,
    CancellationToken cancellationToken = default);
}