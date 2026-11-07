using Platform.Access.Domain.Enums;

namespace Platform.Api.Modules.AssignmentsAccess;

// ASN-04 / ASN-06 TIM 15
// Answers one question for the authorization adapters: what is the state of this account?
// An account that does not exist is reported as AccountState.Unknown, which the access
// engine always denies (deny by default).
public interface IAccountStateReader
{
    ValueTask<AccountState> GetStateAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}
