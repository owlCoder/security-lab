using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Competencies.Contracts;
using Platform.Competencies.Infrastructure.Persistence;

namespace Platform.Competencies.Composition;

public static class CompetenciesServiceCollectionExtensions
{
    // Explicit persistence registration used by Platform.Api; startup does not apply migrations.
    public static IServiceCollection AddCompetencyPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<CompetenciesDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", CompetenciesDbContext.SchemaName)));
        services.AddScoped<PostgresLaboratoryCompetencyStore>();
        services.AddScoped<ILaboratoryCompetencyStore>(provider => provider.GetRequiredService<PostgresLaboratoryCompetencyStore>());
        services.AddScoped<ICompetencyCatalog>(provider => provider.GetRequiredService<PostgresLaboratoryCompetencyStore>());
        return services;
    }
}
