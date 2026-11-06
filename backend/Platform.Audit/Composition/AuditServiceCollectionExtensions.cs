using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Correlation;
using Platform.Audit.Infrastructure.Persistence;

namespace Platform.Audit.Composition;

public static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(provider => provider.GetRequiredService<CorrelationContext>());

        return services;
    }

    /// <summary>
    /// Registers the persistent writer. A missing/invalid runtime connection keeps
    /// startup possible, but writes fail closed as <see cref="AuditWriteStatus.Unavailable"/>.
    /// Migrations are never executed by the runtime account.
    /// </summary>
    public static IServiceCollection AddAudit(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddAudit();

        var connectionString = configuration.GetConnectionString("Audit");
        services.AddDbContext<AuditDbContext>(options =>
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));
        services.AddScoped<IAuditEventStore, PostgresAuditEventStore>();
        services.AddScoped<AuditDraftValidator>();
        services.AddScoped<IAuditContextProvider, UnavailableAuditContextProvider>();
        services.AddScoped<AuditWriter>();
        services.AddScoped<IAuditWriter>(provider => provider.GetRequiredService<AuditWriter>());
        services.AddScoped<ITrustedAuditWriter>(provider => provider.GetRequiredService<AuditWriter>());

        return services;
    }

    private sealed class UnavailableAuditContextProvider : IAuditContextProvider
    {
        public AuditContext? GetCurrentContext() => null;
    }
}
