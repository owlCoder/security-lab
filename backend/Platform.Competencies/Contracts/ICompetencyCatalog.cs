using Platform.Competencies.Domain;

namespace Platform.Competencies.Contracts;

public interface ICompetencyCatalog
{
    Task<IReadOnlyList<Competency>> ListAsync(CancellationToken cancellationToken = default);
}
