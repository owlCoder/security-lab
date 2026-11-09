using Platform.Assignments.Contracts;
 
namespace Platform.AssignmentOverview.Tests.StartCheck;
 
// ASN-07 TIM 16
// Lazni pomocnici samo za testove provere preduslova.
 
internal sealed class StartCheckAuthorization : IStartAttemptAuthorization
{
    private readonly bool _allow;
 
    public StartCheckAuthorization(bool allow)
    {
        _allow = allow;
    }
 
    public ValueTask<bool> CanStartAsync(
        string studentId,
        Guid assignmentId,
        string courseId,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_allow);
}
 
// Glumi skladiste pokusaja: broji koliko puta je pokusaj zaista pokrenut.
internal sealed class StartCheckAttemptStore : IAttemptStore
{
    public int StartCalls { get; private set; }
 
    public ValueTask<Guid?> TryStartAsync(
        Guid assignmentId,
        string studentId,
        string publishedLabRevisionId,
        int maxAttempts,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        StartCalls++;
        return ValueTask.FromResult<Guid?>(Guid.NewGuid());
    }
}