using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using ApiIdentityContext = Platform.Api.Interfaces.Identity.IIdentityContext;

namespace Platform.Api.Modules.Assignments.Api;

// ASN-04 / ASN-06 TIM 15 (celina 5)

/// <summary>
/// Jedini izvor ID-a aktera za Assignments use-case-ove: sesija koju je proverio
/// <c>SessionCheckMiddleware</c> i aktivan nalog. Vrednost iz tela, rute ili zaglavlja se nikad ne
/// koristi kao akter. Vraća <c>null</c> kada pozivalac nije prijavljen ili nalog nije aktivan.
/// </summary>
public sealed class AssignmentCaller(
    ApiIdentityContext identityContext,
    IAccountStatusReader accounts)
{
    public async Task<string?> GetActorIdAsync(CancellationToken cancellationToken = default)
    {
        var identity = identityContext.Current;

        if (identity is not { IsAuthenticated: true, UserId: Guid userId } || userId == Guid.Empty)
        {
            return null;
        }

        var account = await accounts.FindAsync(userId, cancellationToken);

        // Blokiran, arhiviran ili nepoznat nalog ne sme da pokrene pokušaj ni da menja rok.
        return account is { Status: AccountStatus.Active }
            ? userId.ToString("D")
            : null;
    }
}
