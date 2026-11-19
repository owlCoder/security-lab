using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Middleware;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using SessionIdentityContextContract = Platform.Api.Interfaces.Identity.IIdentityContext;

namespace Platform.Api.Modules.Competencies;

// Used only by PRG-04 handlers. In Development the shared session middleware skips
// /api/laboratories, so validate its cookie here if it supplied no trusted session.
public sealed class Prg04SessionIdentity(
    SessionIdentityContextContract identity,
    ISessionManager sessions,
    IAccountStatusReader accounts)
{
    public async Task<Guid?> ResolveAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var caller = identity.Current;
        Guid? id = caller is { IsAuthenticated: true, Level: AuthenticationLevel.Active,
            UserId: { } currentId, CurrentSessionHandle: { } handle } &&
            currentId != Guid.Empty && handle != Guid.Empty ? currentId : null;

        if (id is null)
        {
            // Never replace an already rejected/partial identity with request headers.
            if (caller is not null) return null;
            var token = http.Request.Cookies[SessionCookie.Name];
            if (string.IsNullOrWhiteSpace(token)) return null;
            ValidateSession? session = await sessions.ValidateAsync(token, cancellationToken);
            if (session is not { Level: AuthenticationLevel.Active } ||
                session.UserId == Guid.Empty || session.Handle == Guid.Empty)
                return null;
            http.Items[SessionCheckMiddleware.SessionItemKey] = session;
            id = session.UserId;
        }

        var account = await accounts.FindAsync(id.Value, cancellationToken);
        return account is { Status: AccountStatus.Active } && account.Id == id.Value ? id : null;
    }
}

