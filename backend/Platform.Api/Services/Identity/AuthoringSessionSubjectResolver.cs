using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;

namespace Platform.Api.Services.Identity;

public sealed class AuthoringSessionSubjectResolver(IIdentityContext identityContext) : IRequestSubjectResolver
{
    public SubjectRef? Resolve(HttpContext context)
    {
        var caller = identityContext.Current;
        if(caller is not { IsAuthenticated: true, UserId: Guid userId } || userId == Guid.Empty)
        {
            return null;
        }

        return new SubjectRef(userId.ToString("D"), AccountState.Active);
    }
}