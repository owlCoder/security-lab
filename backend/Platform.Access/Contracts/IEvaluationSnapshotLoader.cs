using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IEvaluationSnapshotLoader
{
    ValueTask<EvaluationSnapshot> LoadAsync(
        AccessRequest request,
        CancellationToken cancellationToken);
}
