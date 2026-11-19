using Platform.Api.Modules.Laboratories.Application;

namespace Platform.Api.Modules.Laboratories.Infrastructure;

public sealed class FakeAccessDecision : IAccessDecision
{
    private static readonly Dictionary<string, PlatformRole[]> AllowedRoles = new()
    {
        [LabActions.List] = [PlatformRole.Student, PlatformRole.Teacher, PlatformRole.Assistant, PlatformRole.Administrator],
        [LabActions.Create] = [PlatformRole.Teacher],
        // LAB-01 API: citanje i preimenovanje nacrta su nastavnicke radnje.
        // Uloga je samo prvi uslov; vlasnistvo nad konkretnom laboratorijom proverava LaboratoryService.
        [LabActions.Read] = [PlatformRole.Teacher],
        [LabActions.Rename] = [PlatformRole.Teacher],
        [LabActions.Search] = [PlatformRole.Student, PlatformRole.Teacher, PlatformRole.Assistant, PlatformRole.Administrator],
    };

    public AccessResult Decide(IIdentityContext subject, string action, string? resourceId = null)
    {
        // 1. Da li je korisnik poznat?
        if (!subject.IsAuthenticated || subject.Role is null)
        {
            return AccessResult.Deny(DenyReason.Unauthenticated);
        }

        // 2. Da li je radnja uopste definisana?
        if (string.IsNullOrEmpty(action) || !AllowedRoles.TryGetValue(action, out PlatformRole[]? roles))
        {
            return AccessResult.Deny(DenyReason.UnknownAction);
        }

        // 3. Da li njegova uloga sme tu radnju?
        if (!roles.Contains(subject.Role.Value))
        {
            return AccessResult.Deny(DenyReason.NotPermitted);
        }

        return AccessResult.Allow();
    }
}