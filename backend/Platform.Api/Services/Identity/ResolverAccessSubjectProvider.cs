using Platform.Access.AspNet;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;

namespace Platform.Api.Services.Identity;

public sealed class ResolverAccessSubjectProvider(IRequestSubjectResolver resolver)
    : IAccessSubjectProvider
{
    public ValueTask<SubjectRef?> GetSubjectAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(resolver.Resolve(httpContext));
}
