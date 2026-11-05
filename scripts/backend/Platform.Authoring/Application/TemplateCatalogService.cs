using Platform.Authoring.Domain;

namespace Platform.Authoring.Application;

/// <summary>Lists the templates an author can start from (AUT-06). Read-only: nothing here can change a template.</summary>
public sealed class TemplateCatalogService(ITemplateRepository templates, IAuthoringAccess access)
{
    /// <exception cref="DomainRuleException">FORBIDDEN.</exception>
    public async Task<IReadOnlyList<LabTemplate>> ListAsync(Guid actorId, CancellationToken ct = default)
    {
        if (actorId == Guid.Empty || !access.IsAllowed(actorId, AuthoringAction.ListTemplates, null))
            throw new DomainRuleException(AuthoringErrorCodes.Forbidden);

        return await templates.ListAsync(ct);
    }
}
