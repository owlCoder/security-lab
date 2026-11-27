using Platform.Identity.Enums;

namespace Platform.Api.Models.Identity;

public sealed record CallerIdentity(
    bool IsAuthenticated,
    Guid? UserId,
    AuthLevel Level,
    Guid? CurrentSessionHandle
    );
