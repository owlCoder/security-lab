using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;

namespace Platform.Api.Services.Identity;

public sealed class LabCaller(
    Platform.Api.Interfaces.Identity.IIdentityContext identityContext,
    IAccountStatusReader accounts)
{
    public async Task<SubjectRef?> GetSubjectAsync(
        CancellationToken cancellationToken = default)
    {
        var identity = identityContext.Current;

        if (identity is null ||
            !identity.IsAuthenticated ||
            identity.UserId is not Guid userId ||
            userId == Guid.Empty)
        {
            return null;
        }

        var account = await accounts.FindAsync(userId, cancellationToken);
        var state = AccountState.Blocked;

        if (account is not null)
        {
            if (account.Status == AccountStatus.Active)
            {
                state = AccountState.Active;
            }
            else if (account.Status == AccountStatus.Archived)
            {
                state = AccountState.Archived;
            }
        }

        return new SubjectRef(userId.ToString("D"), state);
    }
}