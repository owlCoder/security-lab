using Platform.Identity.Enums;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Services.Sessions;

namespace Platform.Api.Services.Identity;

/// US-06: pozivalac se cita iz sesije koju je SessionCheckMiddleware vec proverio.
public sealed class SessionIdentityContext : IIdentityContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SessionIdentityContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CallerIdentity? Current
    {
        get
        {
            var session = _httpContextAccessor.HttpContext?.Items[SessionHttpContextItems.Session] as ValidateSession;

            if (session == null)
                return null;

            return new CallerIdentity(
                session.Level == AuthLevel.Active,
                session.UserId,
                session.Level,
                session.Handle);
        }
    }
}
