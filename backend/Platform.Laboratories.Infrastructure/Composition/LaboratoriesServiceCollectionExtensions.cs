using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Laboratories.Domain.Contracts;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Laboratories.Infrastructure.Composition;

public static class LaboratoriesServiceCollectionExtensions
{
    public static IServiceCollection AddLaboratoriesPersistence(
        this IServiceCollection services,
        string? connectionString)
    {
        services.AddDbContextFactory<LaboratoriesDbContext>(options =>
            options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable(
                "__EFMigrationsHistory", LaboratoriesDbContext.SchemaName)));

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IRevisionDecisionStore, RevisionDecisionStore>();

        return services;
    }
}
