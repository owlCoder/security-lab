using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Modules.Administration.Api;
using Platform.Api.Modules.Administration.Integration;
using Platform.Api.Modules.Administration.Persistence;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Identity;
using Team21.Acc.Infrastructure.Common;
using Team21.Acc.Infrastructure.Courses.Data;

namespace Platform.Coursework.AdminTool;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine("ADM-08 operator: migrate | grant-runtime <role> | bootstrap-root <course-id> <account-id> --confirm-initial-root | relay-once");
            Console.WriteLine("Owner connection: COURSEWORK_MIGRATION_CONNECTION. Provider connections: ConnectionStrings__Identity, ConnectionStrings__Team21, ConnectionStrings__Audit.");
            Console.WriteLine("Bootstrap uses existing active provider records, cannot replace a root, and creates no account or catalog course.");
            return 0;
        }
        var valid = (args[0] == "migrate" && args.Length == 1) ||
            (args[0] == "grant-runtime" && args.Length == 2) ||
            (args[0] == "relay-once" && args.Length == 1) ||
            (args[0] == "bootstrap-root" && args.Length == 4 && args[3] == "--confirm-initial-root" &&
                Guid.TryParse(args[1], out var c) && c != Guid.Empty && Guid.TryParse(args[2], out var a) && a != Guid.Empty);
        if (!valid) { Console.Error.WriteLine("Invalid operator action. Use --help."); return 2; }
        var connection = Environment.GetEnvironmentVariable("COURSEWORK_MIGRATION_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        { Console.Error.WriteLine("COURSEWORK_MIGRATION_CONNECTION is required."); return 2; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            if (args[0] == "migrate") await AdministrationSchema.MigrateAsync(connection, timeout.Token);
            else if (args[0] == "grant-runtime") await AdministrationSchema.GrantRuntimeRoleAsync(connection, args[1], timeout.Token);
            else
            {
                var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton(TimeProvider.System);
                if (args[0] == "bootstrap-root")
                {
                    if (string.IsNullOrWhiteSpace(config.GetConnectionString("Identity")) || string.IsNullOrWhiteSpace(config.GetConnectionString("Team21")))
                        throw new InvalidOperationException("Provider connections are required.");
                    services.AddIdentityModule(config);
                    services.AddTeamDbContext<CoursesDbContext>(config.GetConnectionString("Team21"), CoursesDbContext.SchemaName);
                    services.AddScoped<ICoursesDbContext>(s => s.GetRequiredService<CoursesDbContext>());
                    services.AddSingleton<IdentityAdministrationAccounts>();
                    services.AddSingleton<ITrustedCourseDirectory,CatalogCourseDirectory>();
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(config.GetConnectionString("Audit"))) throw new InvalidOperationException("Audit connection is required.");
                    services.AddAudit(config);
                }
                await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                if (args[0] == "bootstrap-root")
                {
                    var result = await new RootProvisioner(connection,provider.GetRequiredService<ITrustedCourseDirectory>(),
                        provider.GetRequiredService<IdentityAdministrationAccounts>()).BootstrapAsync(Guid.Parse(args[1]),Guid.Parse(args[2]),timeout.Token);
                    Console.WriteLine(result.Created ? "Initial course root provisioned." : "The same course root already exists; no change.");
                }
                else
                {
                    await using var scope=provider.CreateAsyncScope();
                    var result=await new AdministrationAuditRelay(connection,scope.ServiceProvider.GetRequiredService<ITrustedAuditWriter>()).RunOnceAsync(timeout.Token);
                    Console.WriteLine($"Audit relay: acknowledged={result.Acknowledged}, failed={result.Failed}, retained_without_catalog_mapping={result.RetainedWithoutCatalogMapping}.");
                    if (result.Failed != 0) return 1;
                }
            }
            Console.WriteLine("ADM-08 operator action completed.");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Operator action failed. Check confirmed IDs, active providers, existing roots and schema permissions. Credentials are not printed.");
            return 1;
        }
    }
}
