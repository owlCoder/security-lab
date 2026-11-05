using System.Security.Cryptography;
using System.Text;

namespace Platform.Api.Modules.Laboratories.Application;

// Pretvara identitet pozivaoca u Guid koji domen koristi za CreatedBy.
//
// Pravi IDN identitet vec je Guid i koristi se direktno. Razvojni fake identitet je tekst
// (npr. "marko" iz X-Test-User), pa se deterministicki mapira: isti korisnik uvek dobija
// isti Guid, a razliciti korisnici razlicite. Mapiranje nije tajna niti dokaz identiteta;
// identitet je vec proveren pre ovog koraka (AccessCheckFilter).
public static class CallerId
{
    private const string Namespace = "security-lab:laboratories:subject:";

    public static Guid? From(IIdentityContext identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (!identity.IsAuthenticated || string.IsNullOrWhiteSpace(identity.UserId))
        {
            return null;
        }

        string userId = identity.UserId.Trim();
        if (Guid.TryParse(userId, out Guid parsed))
        {
            return parsed == Guid.Empty ? null : parsed;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Namespace + userId));
        return new Guid(hash.AsSpan(0, 16));
    }
}