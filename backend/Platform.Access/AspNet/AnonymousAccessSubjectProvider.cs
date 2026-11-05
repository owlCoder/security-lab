using Microsoft.AspNetCore.Http;
using Platform.Access.Domain.Models;

namespace Platform.Access.AspNet;

public sealed class AnonymousAccessSubjectProvider
    : IAccessSubjectProvider
{
    public ValueTask<SubjectRef?> GetSubjectAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<SubjectRef?>(null);
    }
}