using Platform.Authoring.Domain;

namespace Platform.Authoring.Application;

/// <summary>Read-only access to templates. Authoring code can never modify a template through this port.</summary>
public interface ITemplateRepository
{
    Task<LabTemplate?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LabTemplate>> ListAsync(CancellationToken ct = default);
}
