using Platform.Identity.Enums;

namespace Platform.Api.Models.Identity;

public sealed record ValidateSession(
    Guid Handle,
    Guid UserId,
    AuthLevel Level
);
