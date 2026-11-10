using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.Assignments.Integration;

// ASN-04 / ASN-06 TIM 15

/// <summary>
/// Access modul (ACC-05) pita „kom kursu pripada resurs?” pre nego što proveri ulogu na kursu.
/// Ovaj resolver odgovara za tip <c>assignment</c> čitanjem same dodele iz <see cref="IAssignmentRepository"/>
/// (pouzdan izvor, nikad klijent), a za sve ostale tipove prosleđuje pitanje prethodno registrovanom
/// resolveru (dekorater), pa se ponašanje drugih modula ne menja.
/// <para>
/// Fail-closed: nepoznata dodela, ID koji nije GUID, kurs dodele koji nije GUID ili greška skladišta
/// daju <c>null</c>, a <c>CourseRoleAccessPolicy</c> tada odbija.
/// </para>
/// </summary>
public sealed class AssignmentResourceDescriptorResolver(
    IServiceScopeFactory scopeFactory,
    IResourceDescriptorResolver inner,
    ILogger<AssignmentResourceDescriptorResolver> logger) : IResourceDescriptorResolver, IResourceOwnershipResolver
{
    public async ValueTask<Guid?> ResolveCourseIdAsync(
        ResourceRef resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (!string.Equals(resource.Type, AssignmentAccessGate.AssignmentResourceType, StringComparison.Ordinal))
        {
            return await inner.ResolveCourseIdAsync(resource, cancellationToken);
        }

        if (!AssignmentAccessGate.TryParseId(resource.Id, out var assignmentId))
        {
            return null;
        }

        try
        {
            // Resolver je singleton (deo Access registra), a skladište može biti scoped.
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAssignmentRepository>();
            var assignment = await repository.GetByIdAsync(assignmentId, cancellationToken);

            return assignment is not null && AssignmentAccessGate.TryParseId(assignment.CourseId, out var courseId)
                ? courseId
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(AssignmentAccessGate.DecisionFailedEvent,
                "Assignment course resolution failed with {ExceptionType}; access denied.",
                exception.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Vlasništvo dodele nije definisano (dodela pripada kursu, ne studentu), pa je odgovor za
    /// <c>assignment</c> uvek <c>null</c>; ostali tipovi idu prethodnom resolveru ako ga podržava.
    /// </summary>
    public ValueTask<Guid?> ResolveOwnerIdAsync(
        ResourceRef resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (!string.Equals(resource.Type, AssignmentAccessGate.AssignmentResourceType, StringComparison.Ordinal)
            && inner is IResourceOwnershipResolver ownership)
        {
            return ownership.ResolveOwnerIdAsync(resource, cancellationToken);
        }

        return ValueTask.FromResult<Guid?>(null);
    }
}
