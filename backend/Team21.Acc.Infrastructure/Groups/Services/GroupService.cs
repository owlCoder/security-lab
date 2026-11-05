using Microsoft.EntityFrameworkCore;
using Npgsql;
using Team21.Acc.Core.Common;
using Team21.Acc.Core.Groups;
using Team21.Acc.Core.Groups.Dtos;
using Team21.Acc.Core.Groups.Interfaces;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Core.Interfaces;
using Team21.Acc.Infrastructure.Groups.Data;

namespace Team21.Acc.Infrastructure.Groups.Services;

/// <summary>
/// Group management for the teacher of a course (ACC-06). Every denial answers 404 like a missing
/// course. Members are removed with a soft delete, so the history stays.
/// </summary>
public class GroupService : IGroupService
{
    private readonly IGroupsDbContext _context;
    private readonly ICurrentActor _actor;
    private readonly ICourseMembershipReader _membershipReader;
    private readonly IAccountStatusReader _accountStatusReader;
    private readonly ICourseStateReader _courseStates;

    public GroupService(
        IGroupsDbContext context,
        ICurrentActor actor,
        ICourseMembershipReader membershipReader,
        IAccountStatusReader accountStatusReader,
        ICourseStateReader courseStates)
    {
        _context = context;
        _actor = actor;
        _membershipReader = membershipReader;
        _accountStatusReader = accountStatusReader;
        _courseStates = courseStates;
    }

    public async Task<Result<GroupDto>> CreateGroupAsync(
        Guid courseId,
        string name,
        CancellationToken ct = default)
    {
        var accessError = await CheckTeacherAccessAsync(courseId, ct);
        if (accessError is not null)
            return accessError;

        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Group name is required.");

        if (name != name.Trim())
            return Error.Validation("Group name cannot have leading or trailing whitespace.");

        if (name.Length > Group.NameMaxLength)
            return Error.Validation($"Group name cannot exceed {Group.NameMaxLength} characters.");

        if (name.Any(char.IsControl))
            return Error.Validation("Group name cannot contain control characters.");

        var courseError = await CheckCourseOpenAsync(courseId, ct);
        if (courseError is not null)
            return courseError;

        var nameTaken = await _context.Groups
            .AnyAsync(g =>
                g.CourseId == courseId &&
                g.Status == GroupStatus.Active &&
                g.Name == name, ct);

        if (nameTaken)
            return Error.Conflict("A group with this name already exists in the course.");

        var group = new Group
        {
            CourseId = courseId,
            Name = name,
            CreatedBy = _actor.UserId!.Value
        };

        _context.Groups.Add(group);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Error.Conflict("A group with this name already exists in the course.");
        }

        return Result<GroupDto>.Success(ToDto(group.Id, group.CourseId, group.Name, group.Status));
    }

    public async Task<Result<IReadOnlyList<GroupDto>>> GetGroupsAsync(
        Guid courseId,
        CancellationToken ct = default)
    {
        var accessError = await CheckTeacherAccessAsync(courseId, ct);
        if (accessError is not null)
            return accessError;

        var groups = await _context.Groups
            .AsNoTracking()
            .Where(g => g.CourseId == courseId)
            .OrderBy(g => g.Name)
            .Select(g => new { g.Id, g.CourseId, g.Name, g.Status })
            .ToListAsync(ct);

        return Result<IReadOnlyList<GroupDto>>.Success(
            groups.Select(g => ToDto(g.Id, g.CourseId, g.Name, g.Status)).ToList());
    }

    public async Task<Result<GroupDto>> GetGroupAsync(
        Guid courseId,
        Guid groupId,
        CancellationToken ct = default)
    {
        var accessError = await CheckTeacherAccessAsync(courseId, ct);
        if (accessError is not null)
            return accessError;

        if (groupId == Guid.Empty)
            return Error.Validation("Invalid group ID.");

        var group = await _context.Groups
            .AsNoTracking()
            .Where(g => g.Id == groupId && g.CourseId == courseId)
            .Select(g => new { g.Id, g.CourseId, g.Name, g.Status })
            .FirstOrDefaultAsync(ct);

        if (group is null)
            return Error.NotFound("Group not found.");

        return Result<GroupDto>.Success(ToDto(group.Id, group.CourseId, group.Name, group.Status));
    }

    public async Task<Result<IReadOnlyList<GroupMemberDto>>> GetMembersAsync(
        Guid courseId,
        Guid groupId,
        CancellationToken ct = default)
    {
        var accessError = await CheckTeacherAccessAsync(courseId, ct);
        if (accessError is not null)
            return accessError;

        if (groupId == Guid.Empty)
            return Error.Validation("Invalid group ID.");

        var groupExists = await _context.Groups
            .AnyAsync(g => g.Id == groupId && g.CourseId == courseId, ct);

        if (!groupExists)
            return Error.NotFound("Group not found.");

        // Removed members are soft deleted and left out by the query filter
        var members = await _context.GroupMembers
            .AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new GroupMemberDto(m.Id, m.GroupId, m.UserId, m.CreatedAt))
            .ToListAsync(ct);

        return Result<IReadOnlyList<GroupMemberDto>>.Success(members);
    }

    public async Task<Result<GroupMemberDto>> AddMemberAsync(
        Guid courseId,
        Guid groupId,
        Guid userId,
        CancellationToken ct = default)
    {
        var accessError = await CheckTeacherAccessAsync(courseId, ct);
        if (accessError is not null)
            return accessError;

        if (groupId == Guid.Empty)
            return Error.Validation("Invalid group ID.");

        if (userId == Guid.Empty)
            return Error.Validation("Invalid user ID.");

        var group = await _context.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId && g.CourseId == courseId, ct);

        if (group is null)
            return Error.NotFound("Group not found.");

        if (group.Status != GroupStatus.Active)
            return Error.Conflict("Group is archived.");

        var courseError = await CheckCourseOpenAsync(courseId, ct);
        if (courseError is not null)
            return courseError;

        var membership = await _membershipReader.FindAsync(userId, courseId, ct);

        if (membership is null ||
            !membership.Active ||
            membership.Role != CourseRole.Student)
        {
            return Error.Validation("User is not an active student in this course.");
        }

        if (await _accountStatusReader.GetStatusAsync(userId, ct) != AccountStatus.Active)
            return Error.Validation("Student account is not active.");

        var alreadyMember = await _context.GroupMembers
            .AnyAsync(m => m.GroupId == groupId && m.UserId == userId, ct);

        if (alreadyMember)
            return Error.Conflict("Student is already a member of this group.");

        var member = new GroupMember
        {
            GroupId = groupId,
            UserId = userId,
            CreatedBy = _actor.UserId!.Value
        };

        _context.GroupMembers.Add(member);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Error.Conflict("Student is already a member of this group.");
        }

        return Result<GroupMemberDto>.Success(
            new GroupMemberDto(member.Id, member.GroupId, member.UserId, member.CreatedAt));
    }

    public async Task<Result> RemoveMemberAsync(
        Guid courseId,
        Guid groupId,
        Guid userId,
        CancellationToken ct = default)
    {
        var accessError = await CheckTeacherAccessAsync(courseId, ct);
        if (accessError is not null)
            return accessError;

        if (groupId == Guid.Empty)
            return Error.Validation("Invalid group ID.");

        if (userId == Guid.Empty)
            return Error.Validation("Invalid user ID.");

        var group = await _context.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId && g.CourseId == courseId, ct);

        if (group is null)
            return Error.NotFound("Group not found.");

        // Members of an archived group or course are frozen
        if (group.Status != GroupStatus.Active)
            return Error.Conflict("Group is archived.");

        var courseError = await CheckCourseOpenAsync(courseId, ct);
        if (courseError is not null)
            return courseError;

        var member = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct);

        if (member is null)
            return Error.NotFound("Group member not found.");

        member.IsDeleted = true;
        member.DeletedAt = DateTime.UtcNow;
        member.DeletedBy = _actor.UserId!.Value;
        member.UpdatedBy = _actor.UserId.Value;

        await _context.SaveChangesAsync(ct);

        return Result.Success();
    }

    // New groups and member changes only in an active course (ACC-01 archiving stops new enrollments)
    private async Task<Error?> CheckCourseOpenAsync(Guid courseId, CancellationToken ct) =>
        await _courseStates.GetStateAsync(courseId, ct) switch
        {
            CourseState.Active => null,
            CourseState.Archived => Error.Conflict("Course is archived."),
            _ => CourseNotFound()
        };

    private async Task<Error?> CheckTeacherAccessAsync(Guid courseId, CancellationToken ct)
    {
        if (!_actor.IsAuthenticated || _actor.UserId is not { } userId)
            return Error.Unauthorized("Sign in first.");

        if (courseId == Guid.Empty)
            return Error.Validation("Invalid course ID.");

        // A blocked teacher manages nothing
        if (await _accountStatusReader.GetStatusAsync(userId, ct) != AccountStatus.Active)
            return CourseNotFound();

        // Same answer as a missing course, a denial must not show the course exists
        var membership = await _membershipReader.FindAsync(userId, courseId, ct);

        if (membership is null ||
            !membership.Active ||
            membership.CourseId != courseId ||
            membership.Role != CourseRole.Teacher)
        {
            return CourseNotFound();
        }

        return null;
    }

    private static GroupDto ToDto(Guid id, Guid courseId, string name, GroupStatus status) =>
        new(id, courseId, name, status.ToString());

    // Two requests passed the check at the same time, the unique index caught the second one
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static Error CourseNotFound() => Error.NotFound("Course not found.");
}
