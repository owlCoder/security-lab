using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.LabVisibility.Infrastructure.Persistence;

// Koristi ga samo alat "dotnet ef" (migrations add, database update), nikad aplikacija.
// Connection string dolazi iz promenljive okruženja, nikad iz koda ni iz Gita.
public sealed class LabVisibilityDesignTimeDbContextFactory : IDesignTimeDbContextFactory<LabVisibilityDbContext>
{
    public const string ConnectionStringVariable = "ConnectionStrings__LabVisibility";

    public LabVisibilityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        var options = new DbContextOptionsBuilder<LabVisibilityDbContext>();

        // "migrations add" ne otvara vezu ka bazi, pa connection string tada nije potreban.
        if (string.IsNullOrWhiteSpace(connectionString))
            options.UseNpgsql(Configure);
        else
            options.UseNpgsql(connectionString, Configure);

        return new LabVisibilityDbContext(options.Options);
    }

    internal static void Configure(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", LabVisibilityDbContext.Schema);
}
