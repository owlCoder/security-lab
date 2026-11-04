using Platform.Api.Enums.Identity;
using Platform.Identity.Enums;

namespace Platform.Api.Models.Identity;

public sealed record ChangeStatusResult(ChangeStatusOutcome Outcome, AccountStatus? NewStatus = null);
