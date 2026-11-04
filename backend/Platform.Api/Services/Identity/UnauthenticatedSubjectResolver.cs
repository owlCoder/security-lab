using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;

namespace Platform.Api.Services.Identity;

public sealed class UnauthenticatedSubjectResolver : IRequestSubjectResolver
{
    public SubjectRef? Resolve(HttpContext context) => null;
}