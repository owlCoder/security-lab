using Platform.Access.Domain.Models;

namespace Platform.Api.Interfaces.Identity;

public interface IRequestSubjectResolver
{
    SubjectRef? Resolve(HttpContext context);
}