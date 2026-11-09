using Microsoft.EntityFrameworkCore;
using Npgsql;
using Team21.Acc.Core.Common;
using Team21.Acc.Core.Courses;
using Team21.Acc.Core.Interfaces;
using Team21.Acc.Infrastructure.Courses.Data;

namespace Team21.Acc.Infrastructure.Courses.Services;

/// <summary>
/// Creating and archiving courses (ACC-01). Archiving is a status change, nothing is deleted.
/// </summary>
public sealed class CourseService : ICourseService
{
    private readonly ICoursesDbContext _context;
    private readonly ICurrentActor _actor;
    private readonly ICourseAuthorization _authorization;
    private readonly IAuditTrail _audit;

    public CourseService(
        ICoursesDbContext context,
        ICurrentActor actor,
        ICourseAuthorization authorization,
        IAuditTrail audit)
    {
        _context = context;
        _actor = actor;
        _authorization = authorization;
        _audit = audit;
    }

    public async Task<Result<CourseDto>> CreateAsync(
        CreateCourseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_actor.IsAuthenticated ||
            _actor.UserId is not Guid userId ||
            userId == Guid.Empty)
        {
            return Error.Unauthorized("Sign in first.");
        }

        if (!await _authorization.CanCreateAsync(userId, cancellationToken))
        {
            await _audit.RecordAsync("course.create", Guid.Empty, AuditOutcome.Denied, cancellationToken);
            return Error.Forbidden("Not allowed to create courses.");
        }

        if (string.IsNullOrWhiteSpace(request.Code))
            return Error.Validation("Course code is required.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return Error.Validation("Course name is required.");

        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        var description = request.Description?.Trim() ?? string.Empty;

        if (code.Length > Course.MaxCodeLength)
            return Error.Validation("Course code is too long.");

        if (name.Length > Course.MaxNameLength)
            return Error.Validation("Course name is too long.");

        if (description.Length > Course.MaxDescriptionLength)
            return Error.Validation("Course description is too long.");

        // The unique index below still catches two creates racing each other
        var exists = await _context.Courses
            .AnyAsync(course => course.Code == code, cancellationToken);

        if (exists)
            return Error.Conflict("Course code already exists.");

        var course = new Course(code, name, description, userId);

        _context.Courses.Add(course);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_Courses_Code"
            })
        {
            return Error.Conflict("Course code already exists.");
        }

        await _audit.RecordAsync("course.create", course.Id, AuditOutcome.Success, cancellationToken);

        return new CourseDto(
            course.Id,
            course.Code,
            course.Name,
            course.Description,
            course.Status.ToString(),
            course.CreatedAt,
            course.ArchivedAt
        );
    }

    public async Task<Result> ArchiveAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        if (!_actor.IsAuthenticated ||
            _actor.UserId is not Guid userId ||
            userId == Guid.Empty)
        {
            return Error.Unauthorized("Sign in first.");
        }

        // Same answer as a missing course, a denial must not show the course exists
        if (!await _authorization.CanArchiveAsync(
                userId, courseId, cancellationToken))
        {
            await _audit.RecordAsync("course.archive", courseId, AuditOutcome.Denied, cancellationToken);
            return Error.NotFound("Course not found.");
        }

        var course = await _context.Courses
            .SingleOrDefaultAsync(
                course => course.Id == courseId,
                cancellationToken);

        if (course is null)
            return Error.NotFound("Course not found.");

        if (course.Status == CourseStatus.Archived)
            return Result.Success();

        course.Archive(userId);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(
                "Course was modified concurrently. Please retry.");
        }

        await _audit.RecordAsync("course.archive", course.Id, AuditOutcome.Success, cancellationToken);

        return Result.Success();
    }
    public async Task<Result<CourseDto>> GetCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        // 1. Authentication
        if (!_actor.IsAuthenticated ||
            _actor.UserId is not Guid userId ||
            userId == Guid.Empty)
        {
            return Error.Unauthorized("Sign in first.");
        }

        // 2. Authorization
        // Do not reveal whether a forbidden course exists.
        if (!await _authorization.CanViewAsync(
                userId, courseId, cancellationToken))
        {
            return Error.NotFound("Course not found.");
        }

        // 3. Read the course
        var course = await _context.Courses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == courseId,
                cancellationToken);

        if (course is null)
            return Error.NotFound("Course not found.");

        // 4. Return DTO
        return new CourseDto(
            course.Id,
            course.Code,
            course.Name,
            course.Description,
            course.Status.ToString(),
            course.CreatedAt,
            course.ArchivedAt
        );
    }


    public async Task<Result<IReadOnlyList<CourseDto>>> GetCoursesAsync(
        CancellationToken cancellationToken = default)
    {
        // 1. Authentication
        if (!_actor.IsAuthenticated ||
            _actor.UserId is not Guid userId ||
            userId == Guid.Empty)
        {
            return Error.Unauthorized("Sign in first.");
        }

        // 2. Read courses sorted by code
        var courses = await _context.Courses
            .AsNoTracking()
            .OrderBy(course => course.Code)
            .ToListAsync(cancellationToken);

        // 3. Include only courses the caller is allowed to view
        var visibleCourses = new List<CourseDto>();

        foreach (var course in courses)
        {
            if (!await _authorization.CanViewAsync(
                    userId, course.Id, cancellationToken))
            {
                continue;
            }

            visibleCourses.Add(new CourseDto(
                course.Id,
                course.Code,
                course.Name,
                course.Description,
                course.Status.ToString(),
                course.CreatedAt,
                course.ArchivedAt
            ));
        }

        return Result<IReadOnlyList<CourseDto>>.Success(visibleCourses);
    }
}