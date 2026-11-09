using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Team21.Acc.Infrastructure.Common;

public static class TeamDbContextRegistration
{
    /// <summary>Key of our connection string, read as ConnectionStrings:Team21.</summary>
    public const string ConnectionStringName = "Team21";

    /// <summary>
    /// Registers a context on our connection string with its migration history in its own schema.
    /// Without a connection string the api still starts and only our queries fail.
    /// </summary>
    public static IServiceCollection AddTeamDbContext<TContext>(
        this IServiceCollection services, string? connectionString, string schema)
        where TContext : TeamDbContextBase
    {
        services.AddDbContext<TContext>(options => options.UseTeamNpgsql(connectionString, schema));
        return services;
    }

    public static DbContextOptionsBuilder UseTeamNpgsql(
        this DbContextOptionsBuilder options, string? connectionString, string schema)
    {
        return options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
    }
}
