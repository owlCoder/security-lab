using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;

namespace Platform.Audit.Composition;

public static class AuditPersistenceExtensions
{
    public static IServiceCollection AddAuditPersistence(this IServiceCollection services, string? connectionString)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<AuditDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));
        services.AddScoped<IAuditEventStore, PostgresAuditEventStore>();
        services.TryAddScoped<AuditDraftValidator>();
        // Trusted producers supply an explicit confirmed context. No ambient actor is invented.
        services.TryAddScoped<IAuditContextProvider, NoAmbientAuditContext>();
        services.AddScoped<AuditWriter>();
        services.AddScoped<ITrustedAuditWriter>(provider => provider.GetRequiredService<AuditWriter>());
        services.AddScoped<IAuditWriter>(provider => provider.GetRequiredService<AuditWriter>());
        services.TryAddSingleton<ITransactionalAuditWriter, PostgresTransactionalAuditWriter>();
        // AUD-06: read side. The reader records every view before it returns data.
        services.AddScoped<IAuditEventQueryStore, PostgresAuditEventQueryStore>();
        services.AddScoped<IAuditCourseReader, AuditCourseReader>();
        return services;
    }

    /// <summary>Explicit deployment/test operation. The web host does not migrate production automatically.</summary>
    public static async Task MigrateAuditDatabaseAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit")).Options;
        await using var db = new AuditDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
    }
}

internal sealed class NoAmbientAuditContext : IAuditContextProvider
{
    public AuditContext? GetCurrentContext() => null;
}
