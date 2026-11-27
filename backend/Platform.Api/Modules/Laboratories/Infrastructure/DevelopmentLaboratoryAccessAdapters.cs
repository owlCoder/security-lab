using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;
using Platform.LabVisibility.Contracts;
using Microsoft.Extensions.DependencyInjection;
using AccessCourseRole = Platform.Access.Domain.Enums.CourseRole;
using LabCourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.Api.Modules.Laboratories.Infrastructure;

/// <summary>
/// Bridges the Development-only LAB visibility membership seed to the central Access contract.
/// Production continues to use the configured central membership reader.
/// </summary>
public sealed class DevelopmentLaboratoryMembershipReader(ICourseMembership memberships) : IMembershipReader
{
    public async ValueTask<CourseMembership?> GetAsync(
        Guid accountId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var role = await memberships.GetRoleAsync(courseId, accountId, cancellationToken);
        var accessRole = role switch
        {
            LabCourseRole.Student => AccessCourseRole.Student,
            LabCourseRole.Teacher => AccessCourseRole.Teacher,
            _ => AccessCourseRole.Unknown,
        };

        return accessRole == AccessCourseRole.Unknown
            ? null
            : new CourseMembership(
                accountId,
                courseId,
                accessRole,
                MembershipStatus.Active,
                new HashSet<string>(StringComparer.Ordinal));
    }
}

/// <summary>
/// Resolves the course for a laboratory resource using the same repository used by LAB routes.
/// This keeps the central course-role policy working for newly created Development drafts.
/// </summary>
public sealed class DevelopmentLaboratoryResourceDescriptorResolver(IServiceScopeFactory scopeFactory)
    : IResourceDescriptorResolver
{
    public async ValueTask<Guid?> ResolveCourseIdAsync(
        ResourceRef resource,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(resource.Type, "laboratory", StringComparison.Ordinal) ||
            !Guid.TryParse(resource.Id, out var laboratoryId) || laboratoryId == Guid.Empty)
        {
            return null;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>();
        var laboratory = await repository.GetByIdAsync(laboratoryId, cancellationToken);
        return laboratory is null || laboratory.CourseId == Guid.Empty
            ? null
            : laboratory.CourseId;
    }
}
