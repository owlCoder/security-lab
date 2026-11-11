using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.Composition;
using Platform.Access.Domain.Constants;
using Platform.Audit.Application;
using Platform.Audit.Application.Authorization;
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

        // Access owns the central action catalog and singleton policy registry. Audit supplies
        // the course-scoped read policy through the public composition hook.
        services.AddAccessPolicy<CourseAuditReadPolicy>(AccessActions.AuditEventsRead.Id);

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
        // Transactional producers (for example REV-05) supply the caller-owned
        // PostgreSQL transaction, so this registration is independent of the
        // regular Audit connection string above.
        services.TryAddSingleton<ITransactionalAuditWriter, PostgresTransactionalAuditWriter>();

        return services;
    }

    private sealed class UnavailableAuditContextProvider : IAuditContextProvider
    {
        public AuditContext? GetCurrentContext() => null;
    }
}
