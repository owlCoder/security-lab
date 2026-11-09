using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Team21.Acc.Core.Interfaces;

namespace Team21.Acc.Api.Services;

/// <summary>
/// Development only. Reads the caller from <c>X-Test-User: {guid}</c>. No header, a bad guid
/// or more than one header means nobody.
/// </summary>
public sealed class HeaderCurrentActor : ICurrentActor
{
    public const string HeaderName = "X-Test-User";

    public HeaderCurrentActor(IHttpContextAccessor accessor)
    {
        var headers = accessor.HttpContext?.Request.Headers;
        if (headers is null || !headers.TryGetValue(HeaderName, out StringValues values) || values.Count != 1)
            return;

        if (Guid.TryParse(values[0], out var userId) && userId != Guid.Empty)
            UserId = userId;
    }

    public Guid? UserId { get; }

    public bool IsAuthenticated => UserId is not null;
}
