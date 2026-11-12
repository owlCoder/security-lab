using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;

namespace Platform.Api.Modules.Laboratories.Api;

// #185: centralni ACC-05 middleware dobija subjekat iz istog identiteta koji koristi modul
// (IIdentityContext: X-Test-User u Development-u, niko van njega).
// Vazi samo za rute cija je RequireAccess radnja jedna od LabActions; sve ostale rute
// (i LabVisibility rute pod /api/labs) idu originalnom provajderu.
public sealed class LaboratorySubjectProvider(IAccessSubjectProvider inner) : IAccessSubjectProvider
{
    public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        if (!IsLaboratoryRoute(httpContext))
        {
            return inner.GetSubjectAsync(httpContext, cancellationToken);
        }

        IIdentityContext identity = httpContext.RequestServices.GetRequiredService<IIdentityContext>();
        Guid? callerId = CallerId.From(identity);

        // Neprijavljen pozivalac nema subjekat, pa middleware vraca 401 kao i ranije.
        SubjectRef? subject = callerId is null
            ? null
            : new SubjectRef(callerId.Value.ToString("D"), AccountState.Active);

        return ValueTask.FromResult(subject);
    }

    // Ruta se prepoznaje po metapodatku (RequireAccess sa laboratory.* radnjom), ne po putanji.
    public static bool IsLaboratoryRoute(HttpContext httpContext)
    {
        string? action = httpContext.GetEndpoint()?.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action;
        return action is not null && LabActions.All.Contains(action);
    }
}
