using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;
using LabAccessDecision = Platform.Api.Modules.Laboratories.Application.IAccessDecision;

namespace Platform.Api.Modules.Laboratories.Api;

// #185: centralna ACC-05 politika za laboratory.* radnje.
// Ponavlja postojecu LAB proveru: pita isti IAccessDecision koji koristi RequireLabAccess,
// pa centralna odluka ne olabavljuje pravila modula. RequireLabAccess i provere u servisu ostaju.
public sealed class LaboratoryAccessPolicy(
    IHttpContextAccessor httpContextAccessor,
    LabAccessDecision labAccess) : IAccessPolicy
{
    public string Id => "laboratory.route";

    public string Version => "1";

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        HttpContext? http = httpContextAccessor.HttpContext;

        // Bez HTTP konteksta, bez subjekta ili sa resursom koji ova radnja ne ocekuje: odbij.
        if (http is null || request.Subject is null || request.Resource is not null)
        {
            return ValueTask.FromResult(false);
        }

        IIdentityContext identity = http.RequestServices.GetRequiredService<IIdentityContext>();
        return ValueTask.FromResult(labAccess.Decide(identity, request.Action).Allowed);
    }
}
