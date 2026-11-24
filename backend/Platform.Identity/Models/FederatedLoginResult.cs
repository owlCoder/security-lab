using Platform.Identity.Enums;

namespace Platform.Identity.Models;

public sealed record FederatedLoginResult(FederatedLoginOutcome Outcome, Guid? AccountId)
{
    public bool IsSuccess => Outcome is FederatedLoginOutcome.Linked or FederatedLoginOutcome.NewlyLinked;

    public static FederatedLoginResult Existing(Guid accountId) => new(FederatedLoginOutcome.Linked, accountId);
    public static FederatedLoginResult New(Guid accountId) => new(FederatedLoginOutcome.NewlyLinked, accountId);
    public static FederatedLoginResult Refused(FederatedLoginOutcome outcome) => new(outcome, null);
}