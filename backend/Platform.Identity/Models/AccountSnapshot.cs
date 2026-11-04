using Platform.Identity.Enums;

namespace Platform.Identity.Models;

public sealed record AccountSnapshot(Guid Id, AccountStatus Status);
