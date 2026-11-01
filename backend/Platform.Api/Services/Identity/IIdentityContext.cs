using Platform.Api.Domain.Authentication;

namespace Platform.Api.Services.Identity;

public sealed record CallerIdentity(bool IsAuthenticated, Guid? UserId, AuthenticationLevel Level, Guid? CurrentSessionHandle);

public interface IIdentityContext
{
    CallerIdentity? Current { get; }
}