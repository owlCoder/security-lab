using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Team21.Acc.Api.Access;

/// <summary>
/// Route level check for our ACC-05 actions: the caller has an active account. Who may do what
/// in a course or a group is decided by our services, a policy can't use their scoped data.
/// </summary>
public abstract class ActiveAccountPolicy(ActionDefinition action) : IAccessPolicy
{
    public string Id => action.Id;

    public string Version => "1";

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Subject is { State: AccountState.Active } && request.Resource is null);
}

public sealed class CourseReadPolicy() : ActiveAccountPolicy(AccessActions.CourseRead);

public sealed class CourseCreatePolicy() : ActiveAccountPolicy(AccessActions.CourseCreate);

public sealed class CourseArchivePolicy() : ActiveAccountPolicy(AccessActions.CourseArchive);

public sealed class GroupReadPolicy() : ActiveAccountPolicy(AccessActions.GroupRead);

public sealed class GroupCreatePolicy() : ActiveAccountPolicy(AccessActions.GroupCreate);

public sealed class GroupMemberAddPolicy() : ActiveAccountPolicy(AccessActions.GroupMemberAdd);

public sealed class GroupMemberRemovePolicy() : ActiveAccountPolicy(AccessActions.GroupMemberRemove);

public sealed class MaterialReadPolicy() : ActiveAccountPolicy(AccessActions.MaterialRead);

public static class ActiveAccountPolicies
{
    public static IServiceCollection AddTeam21AccessPolicies(this IServiceCollection services) =>
        services
            .AddAccessPolicy<CourseReadPolicy>(AccessActions.CourseRead.Id)
            .AddAccessPolicy<CourseCreatePolicy>(AccessActions.CourseCreate.Id)
            .AddAccessPolicy<CourseArchivePolicy>(AccessActions.CourseArchive.Id)
            .AddAccessPolicy<GroupReadPolicy>(AccessActions.GroupRead.Id)
            .AddAccessPolicy<GroupCreatePolicy>(AccessActions.GroupCreate.Id)
            .AddAccessPolicy<GroupMemberAddPolicy>(AccessActions.GroupMemberAdd.Id)
            .AddAccessPolicy<GroupMemberRemovePolicy>(AccessActions.GroupMemberRemove.Id)
            .AddAccessPolicy<MaterialReadPolicy>(AccessActions.MaterialRead.Id);
}
