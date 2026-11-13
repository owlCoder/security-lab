using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Team21.Acc.Api;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Core.Interfaces;

namespace Platform.Api.Modules.Team21.Integration;

/// <summary>
/// Supplies Access with the validated Team 21 actor and the existing account-reader result.
/// Unmarked endpoints retain their original provider; authentication remains IDN's responsibility.
/// </summary>
public sealed class Team21AccessSubjectProvider(
    IAccessSubjectProvider inner,
    Func<ICurrentActor> actorFactory,
    Func<IAccountStatusReader> accountsFactory) : IAccessSubjectProvider
{
    public async ValueTask<SubjectRef?> GetSubjectAsync(
        HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<Team21SessionRequiredMetadata>() is null)
            return await inner.GetSubjectAsync(httpContext, cancellationToken);

        var actor = actorFactory();
        if (!actor.IsAuthenticated || actor.UserId is not { } userId || userId == Guid.Empty)
            return null;

        // AuthenticationLevel.Active is not evidence of an active account.
        var status = await accountsFactory().GetStatusAsync(userId, cancellationToken);
        var state = status switch
        {
            AccountStatus.Active => AccountState.Active,
            AccountStatus.Blocked => AccountState.Blocked,
            // Inactive has no exact Access enum counterpart. Unknown also fails closed.
            _ => AccountState.Unknown
        };
        return new SubjectRef(userId.ToString("D"), state);
    }
}
