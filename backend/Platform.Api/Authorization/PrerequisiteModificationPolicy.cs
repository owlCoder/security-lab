
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Authoring.Application;

namespace Platform.Api.Authorization;

public sealed class PrerequisiteModificationPolicy(
    IServiceScopeFactory scopeFactory,
    IAuthorDirectory authors) : IAccessPolicy
{
    public string Id => "prerequisites.author-only";
    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Subject is null ||
            !Guid.TryParse(request.Subject.Id, out var userId) ||
            userId == Guid.Empty ||
            !authors.IsAuthor(userId) ||
            request.Resource is null ||
            request.Resource.Type != "laboratory" ||
            !Guid.TryParse(request.Resource.Id, out var laboratoryId) ||
            laboratoryId == Guid.Empty)
        {
            return false;
        }

        using var scope = scopeFactory.CreateScope();

        var laboratories = scope.ServiceProvider
            .GetRequiredService<ILaboratoryRepository>();

        var laboratory = await laboratories.GetByIdAsync(
            laboratoryId, cancellationToken);

        return laboratory is not null &&
               laboratory.CreatedBy == userId;
    }
}
