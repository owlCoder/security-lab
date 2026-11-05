using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.LaboratoryPersistence.Application;
using Platform.LaboratoryPersistence.Infrastructure.Persistence;

namespace Platform.LaboratoryPersistence.Composition;

public static class LaboratoryPersistenceExtensions
{
    public static IServiceCollection AddLaboratoryPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Authoring")
            ?? throw new InvalidOperationException("ConnectionStrings:Authoring is required for laboratory persistence.");
        services.AddDbContext<LaboratoryDbContext>(o => o.UseNpgsql(connectionString,
            n => n.MigrationsHistoryTable("__EFMigrationsHistory", "authoring")));
        services.AddScoped<PostgresLaboratoryStore>();
        services.AddScoped<ILaboratoryStore>(s => s.GetRequiredService<PostgresLaboratoryStore>());
        services.AddScoped<ILaboratoryMetadataStore>(s => s.GetRequiredService<PostgresLaboratoryStore>());
        services.AddScoped<ILabPrerequisiteStore>(s => s.GetRequiredService<PostgresLaboratoryStore>());
        return services;
    }
}
