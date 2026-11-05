using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Laboratories.Infrastructure.Persistence;

public sealed class LaboratoriesDbContextFactory : IDesignTimeDbContextFactory<LaboratoriesDbContext>
{
    public LaboratoriesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Laboratories");
        var options = new DbContextOptionsBuilder<LaboratoriesDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable(
                "__EFMigrationsHistory", LaboratoriesDbContext.SchemaName))
            .Options;

        return new LaboratoriesDbContext(options);
    }
}
