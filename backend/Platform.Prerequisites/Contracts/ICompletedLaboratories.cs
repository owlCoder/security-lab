namespace Platform.Prerequisites.Contracts;

public interface ICompletedLaboratories
{
    Task<IReadOnlySet<Guid>> GetCompletedAsync(
        Guid studentId,
        CancellationToken cancellationToken = default);
}