using Microsoft.AspNetCore.Identity;
using Platform.Identity.Enums;

namespace Platform.Identity.Entities;

public sealed class AppUser : IdentityUser<Guid>
{
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}