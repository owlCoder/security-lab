using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Team21.Acc.Core.Courses;
using Team21.Acc.Infrastructure.Courses.Authorization;
using Team21.Acc.Infrastructure.Courses.Services;

namespace Team21.Acc.Api.Courses;

public static class CoursesModule
{
    public static IServiceCollection AddCourses(
        this IServiceCollection services)
    {
        services.AddScoped<ICourseService, CourseService>();

        services.AddScoped<ICourseAuthorization>(provider =>
        {
            var environment =
                provider.GetRequiredService<IHostEnvironment>();

            if (environment.IsDevelopment())
                return new DevelopmentCourseAuthorization();

            return new DenyAllCourseAuthorization();
        });

        return services;
    }

    public static IEndpointRouteBuilder MapCourses(
        this IEndpointRouteBuilder app)
    {
        app.MapCourseEndpoints();

        return app;
    }
}