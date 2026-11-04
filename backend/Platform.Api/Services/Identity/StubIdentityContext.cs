using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Api.Services.Identity;

/// DEV STUB do IDN-01/IDN-02: pozivalac se cita iz headera X-Stub-User.
/// Registruje se SAMO u Development okruzenju.
public sealed class StubIdentityContext(IHttpContextAccessor http) : IIdentityContext
{
    public CallerIdentity? Current
    {
        get
        {
            var raw = http.HttpContext?.Request.Headers["X-Stub-User"].FirstOrDefault();
            return Guid.TryParse(raw, out var id) ? new CallerIdentity(true, id, AuthenticationLevel.None, Guid.NewGuid()) : null;
        }
    }
}
