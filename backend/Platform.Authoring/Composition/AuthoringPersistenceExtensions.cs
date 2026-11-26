using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Authoring.Application;
using Platform.Authoring.Infrastructure.Persistence;

namespace Platform.Authoring.Composition;

public static class AuthoringPersistenceExtensions
{
    /// <summary>
    /// Call after AddAuthoring(): swaps the in-memory draft store for PostgreSQL. The use-case services
    /// become scoped because the DbContext is scoped (docs/components/audit/DI-GUIDELINES.md).
    /// </summary>
    public static IServiceCollection AddAuthoringPersistence(this IServiceCollection services, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Authoring is not configured.");

        services.AddDbContext<AuthoringDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", AuthoringDbContext.Schema)));

        services.Replace(ServiceDescriptor.Scoped<IDraftRepository, PostgresDraftRepository>());
        services.Replace(ServiceDescriptor.Scoped<DraftService, DraftService>());
        services.Replace(ServiceDescriptor.Scoped<TemplateCatalogService, TemplateCatalogService>());
        return services;
    }

    /// <summary>Explicit deployment/test operation. The web host does not migrate production automatically.</summary>
    public static async Task MigrateAuthoringDatabaseAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<AuthoringDbContext>().UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", AuthoringDbContext.Schema)).Options;
        await using var db = new AuthoringDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
