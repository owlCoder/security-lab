using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Team21.Acc.Infrastructure.Courses.Data;
using Team21.Acc.Infrastructure.Groups.Fakes;

namespace Team21.Acc.Api.Groups;

/// <summary>
/// Development only. Seeds the demo courses and the ACC-06 demo data at startup. A missing or unmigrated database is
/// logged and the api starts anyway.
/// </summary>
public sealed class DemoSeedHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DemoSeedHostedService> _logger;

    public DemoSeedHostedService(
        IServiceScopeFactory scopes,
        IHostEnvironment environment,
        ILogger<DemoSeedHostedService> logger)
    {
        _scopes = scopes;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var courses = scope.ServiceProvider.GetRequiredService<ICoursesDbContext>();
            await CourseDevelopmentSeed.SeedAsync(courses, _environment.EnvironmentName, cancellationToken);

            var seeder = scope.ServiceProvider.GetRequiredService<DemoGroupsSeeder>();

            if (await seeder.SeedAsync(cancellationToken))
                _logger.LogInformation("ACC-06 demo groups and materials seeded");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Demo seed skipped, are the courses and groups migrations applied?");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
