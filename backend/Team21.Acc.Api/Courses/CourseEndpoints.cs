using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Platform.Access.AspNet;
using Platform.Access.Domain.Constants;
using Team21.Acc.Api.Extensions;
using Team21.Acc.Core.Courses;
namespace Team21.Acc.Api.Courses;

/// <summary>
/// ACC-01 endpoints. Creating a course without the right answers 403, archiving one answers 404.
/// </summary>
public static class CourseEndpoints
{
    public static IEndpointRouteBuilder MapCourseEndpoints(
        this IEndpointRouteBuilder app)
    {
        // ACC-05 checks for an active account, the service decides the rest
        var group = app.MapGroup("/api/courses");

         group.MapGet("", GetCourses)
            .WithName("GetCourses")
            .RequireAccess(AccessActions.CourseRead.Id);

        group.MapGet("/{id:guid}", GetCourse)
            .WithName("GetCourse")
            .RequireAccess(AccessActions.CourseRead.Id);

        group.MapPost("", CreateCourse)
            .WithName("CreateCourse")
            .RequireAccess(AccessActions.CourseCreate.Id);

        group.MapPost("/{id:guid}/archive", ArchiveCourse)
            .WithName("ArchiveCourse")
            .RequireAccess(AccessActions.CourseArchive.Id);

        return app;
    }

    private static async Task<IResult> CreateCourse(
    CreateCourseRequest request,
    ICourseService courseService,
    CancellationToken cancellationToken)
    {
        var result = await courseService.CreateAsync(
            request,
            cancellationToken);

        return result.ToCreatedHttpResult(
            course => $"/api/courses/{course.Id}");
    }

    private static async Task<IResult> ArchiveCourse(
        Guid id,
        ICourseService courseService,
        CancellationToken cancellationToken)
    {
        var result = await courseService.ArchiveAsync(
            id,
            cancellationToken);

        return result.ToHttpResult();
    }


private static async Task<IResult> GetCourses(
    ICourseService courseService,
    CancellationToken cancellationToken)
{
    var result = await courseService.GetCoursesAsync(
        cancellationToken);

    return result.ToHttpResult();
}

private static async Task<IResult> GetCourse(
    Guid id,
    ICourseService courseService,
    CancellationToken cancellationToken)
{
    var result = await courseService.GetCourseAsync(
        id,
        cancellationToken);

    return result.ToHttpResult();
}

}