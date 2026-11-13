using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Team21.Acc.Core.Groups;
using Team21.Acc.Core.Groups.Interfaces;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Infrastructure.Courses.Services;
using Team21.Acc.Infrastructure.Groups.Defaults;
using Team21.Acc.Infrastructure.Groups.Fakes;
using Team21.Acc.Infrastructure.Groups.Services;

namespace Team21.Acc.Api.Groups;

/// <summary>
/// ACC-06 services and endpoints are registered here.
/// </summary>
public static class GroupsModule
{
    public static IServiceCollection AddGroups(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IGroupMembershipReader, GroupMembershipReader>();
        services.AddScoped<IMaterialAccessRuleReader, MaterialAccessRuleReader>();

        // ACC-01 answers for the course in every environment
        services.AddScoped<ICourseStateReader, CourseStateReader>();

        // Other teams' modules: fakes in Development, deny-all defaults elsewhere
        if (environment.IsDevelopment())
        {
            services.AddSingleton<FakeDirectory>();
            services.AddScoped<IAccountStatusReader, FakeAccountStatusReader>();
            services.AddScoped<ICourseMembershipReader, FakeCourseMembershipReader>();

            services.AddScoped<DemoGroupsSeeder>();
            services.AddHostedService<DemoSeedHostedService>();
        }
        else
        {
            services.AddScoped<IAccountStatusReader, DenyAllAccountStatusReader>();
            services.AddScoped<ICourseMembershipReader, NoCourseMembershipReader>();
        }

        services.AddScoped<IVisibilityAuditWriter, LoggingVisibilityAuditWriter>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IMaterialVisibilityService, MaterialVisibilityService>();

        return services;
    }

    public static IEndpointRouteBuilder MapGroups(this IEndpointRouteBuilder app)
    {
        app.MapGroupEndpoints();
        app.MapMaterialEndpoints();

        return app;
    }
}
