using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Team21.Acc.Api.Access;
using Team21.Acc.Api.Courses;
using Team21.Acc.Api.Groups;
using Team21.Acc.Api.Services;
using Team21.Acc.Core.Interfaces;
using Team21.Acc.Infrastructure.Common;
using Team21.Acc.Infrastructure.Courses.Data;
using Team21.Acc.Infrastructure.Groups.Data;

namespace Team21.Acc.Api;

/// <summary>
/// Everything team 21 registers. Platform.Api only calls AddTeam21Acc() and MapTeam21Acc().
/// </summary>
public static class Team21AccModule
{
    public static WebApplicationBuilder AddTeam21Acc(this WebApplicationBuilder builder)
    {
        var services = builder.Services;

        // Platform.Api has no user secrets, ours are read in Development
        if (builder.Environment.IsDevelopment())
            builder.Configuration.AddUserSecrets(typeof(TeamDbContextBase).Assembly, optional: true);

        services.AddHttpContextAccessor();

        // Header fake only in Development
        if (builder.Environment.IsDevelopment())
            services.AddScoped<ICurrentActor, HeaderCurrentActor>();
        else
            services.AddScoped<ICurrentActor, AnonymousCurrentActor>();

        services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAuditTrail, LoggingAuditTrail>();

        var connectionString = builder.Configuration.GetConnectionString(TeamDbContextRegistration.ConnectionStringName);

        services.AddTeamDbContext<CoursesDbContext>(connectionString, CoursesDbContext.SchemaName);
        services.AddScoped<ICoursesDbContext>(provider => provider.GetRequiredService<CoursesDbContext>());

        services.AddTeamDbContext<GroupsDbContext>(connectionString, GroupsDbContext.SchemaName);
        services.AddScoped<IGroupsDbContext>(provider => provider.GetRequiredService<GroupsDbContext>());

        services.AddTeam21AccessPolicies();
        services.AddCourses();
        services.AddGroups(builder.Environment);

        return builder;
    }

    public static IEndpointRouteBuilder MapTeam21Acc(this IEndpointRouteBuilder app)
    {
        var endpoints = app.MapGroup("").WithMetadata(new Team21SessionRequiredMetadata());
        endpoints.MapCourses();
        endpoints.MapGroups();

        return app;
    }
}
