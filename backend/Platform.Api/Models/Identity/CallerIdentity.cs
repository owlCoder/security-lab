using Platform.Api.Enums.Identity;

namespace Platform.Api.Models.Identity;

public sealed record CallerIdentity(
    bool IsAuthenticated,
    Guid? UserId,
    AuthenticationLevel Level,
    Guid? CurrentSessionHandle
    );
