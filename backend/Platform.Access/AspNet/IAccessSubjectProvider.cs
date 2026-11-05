using Microsoft.AspNetCore.Http;
using Platform.Access.Domain.Models;

namespace Platform.Access.AspNet;

public interface IAccessSubjectProvider
{
    ValueTask<SubjectRef?> GetSubjectAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}