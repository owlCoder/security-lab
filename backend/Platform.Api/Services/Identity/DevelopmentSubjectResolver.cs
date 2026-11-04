using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;

namespace Platform.Api.Services.Identity;

public sealed class DevelopmentSubjectResolver(IConfiguration configuration) : IRequestSubjectResolver
{
    public SubjectRef? Resolve(HttpContext context)
    {
        var id = configuration["Identity:DevSubjectId"];
        return string.IsNullOrWhiteSpace(id) ? null : new SubjectRef(id, AccountState.Active);
    }
}