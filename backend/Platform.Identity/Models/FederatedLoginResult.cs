using Platform.Identity.Enums;

namespace Platform.Identity.Models;

public sealed record FederatedLoginResult(FederatedLoginOutcome Outcome, Guid? AccountId, AccountRole Role = default)
{
    public bool IsSuccess => Outcome is FederatedLoginOutcome.Linked or FederatedLoginOutcome.NewlyLinked;

    public static FederatedLoginResult Existing(Guid accountId, AccountRole role = default) =>
        new(FederatedLoginOutcome.Linked, accountId, role);
    public static FederatedLoginResult New(Guid accountId, AccountRole role = default) =>
        new(FederatedLoginOutcome.NewlyLinked, accountId, role);
    public static FederatedLoginResult Refused(FederatedLoginOutcome outcome) => new(outcome, null);
}