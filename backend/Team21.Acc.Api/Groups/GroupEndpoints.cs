using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Platform.Access.AspNet;
using Platform.Access.Domain.Constants;
using Team21.Acc.Api.Extensions;
using Team21.Acc.Core.Groups.Interfaces;

namespace Team21.Acc.Api.Groups;

/// <summary>
/// ACC-06 group management over HTTP, for the teacher of the course. A denial answers 404.
/// </summary>
public static class GroupEndpoints
{
    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        // ACC-05 checks for an active account, the service decides the rest
        var group = app.MapGroup("/api/courses/{courseId:guid}/groups");

        group.MapGet("/", GetGroupsAsync)
            .RequireAccess(AccessActions.GroupRead.Id);
        group.MapPost("/", CreateGroupAsync)
            .RequireAccess(AccessActions.GroupCreate.Id);
        group.MapGet("/{groupId:guid}", GetGroupAsync)
            .RequireAccess(AccessActions.GroupRead.Id);
        group.MapGet("/{groupId:guid}/members", GetMembersAsync)
            .RequireAccess(AccessActions.GroupRead.Id);
        group.MapPost("/{groupId:guid}/members", AddMemberAsync)
            .RequireAccess(AccessActions.GroupMemberAdd.Id);
        group.MapDelete("/{groupId:guid}/members/{userId:guid}", RemoveMemberAsync)
            .RequireAccess(AccessActions.GroupMemberRemove.Id);

        return app;
    }

    private static async Task<IResult> GetGroupsAsync(
        Guid courseId, IGroupService groupService, CancellationToken ct)
    {
        var result = await groupService.GetGroupsAsync(courseId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateGroupAsync(
        Guid courseId, CreateGroupRequest request, IGroupService groupService, CancellationToken ct)
    {
        var result = await groupService.CreateGroupAsync(courseId, request.Name ?? string.Empty, ct);
        return result.ToCreatedHttpResult(created => $"/api/courses/{courseId}/groups/{created.Id}");
    }

    private static async Task<IResult> GetGroupAsync(
        Guid courseId, Guid groupId, IGroupService groupService, CancellationToken ct)
    {
        var result = await groupService.GetGroupAsync(courseId, groupId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetMembersAsync(
        Guid courseId, Guid groupId, IGroupService groupService, CancellationToken ct)
    {
        var result = await groupService.GetMembersAsync(courseId, groupId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AddMemberAsync(
        Guid courseId, Guid groupId, AddGroupMemberRequest request, IGroupService groupService, CancellationToken ct)
    {
        var result = await groupService.AddMemberAsync(courseId, groupId, request.UserId, ct);
        return result.ToCreatedHttpResult(
            member => $"/api/courses/{courseId}/groups/{groupId}/members/{member.UserId}");
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid courseId, Guid groupId, Guid userId, IGroupService groupService, CancellationToken ct)
    {
        var result = await groupService.RemoveMemberAsync(courseId, groupId, userId, ct);
        return result.ToHttpResult();
    }
}

/// <summary>Body of POST .../groups/{groupId}/members.</summary>
public sealed record AddGroupMemberRequest(Guid UserId);

/// <summary>Body of POST .../groups. The name is not trimmed, surrounding spaces are rejected.</summary>
public sealed record CreateGroupRequest(string? Name);
