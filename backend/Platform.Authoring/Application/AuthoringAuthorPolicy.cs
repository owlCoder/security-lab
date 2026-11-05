using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Authoring.Application;

/// <summary>
/// Access policy for the template actions: only an authenticated account that is on the author list.
/// The Access module has already rejected missing, inactive and unknown-action requests by the time
/// a policy runs, and it treats a thrown exception as a denial.
/// </summary>
public sealed class AuthoringAuthorPolicy(IAuthorDirectory authors) : IAccessPolicy
{
    public string Id => "authoring.author-only";
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        var allowed = request.Subject is not null
            && Guid.TryParse(request.Subject.Id, out var userId)
            && userId != Guid.Empty
            && authors.IsAuthor(userId);

        return ValueTask.FromResult(allowed);
    }
}
