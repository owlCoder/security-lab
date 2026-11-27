using Platform.Access.AspNet;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;

namespace Platform.Api.Modules.Laboratories.Api;

// #185: centralni ACC-05 middleware dobija subjekat iz istog identiteta koji koristi modul
// (IIdentityContext: X-Test-User u Development-u, niko van njega).
// Važi za LAB-01 i LAB-02 draft-management rute; sve ostale rute
// (i LabVisibility rute pod /api/labs) idu originalnom provajderu.
public sealed class LaboratorySubjectProvider(
    IAccessSubjectProvider inner,
    IHostEnvironment? environment = null) : IAccessSubjectProvider
{
    public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        if (!IsLaboratoryRoute(httpContext))
        {
            return inner.GetSubjectAsync(httpContext, cancellationToken);
        }

        // Metadata draft authorization is course-scoped in the central Access module.
        // Outside Development, preserve the real session subject provider instead of
        // attempting to read the Development-only FakeIdentityContext.
        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action == AccessActions.LabDraftManage.Id &&
            environment?.IsDevelopment() == false)
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
        return action is not null &&
               (LabActions.All.Contains(action) || action == AccessActions.LabDraftManage.Id);
    }
}
