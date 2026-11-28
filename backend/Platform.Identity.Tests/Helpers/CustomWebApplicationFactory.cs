using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Integration;

namespace Platform.Identity.Tests.Helpers;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // #113: the Audit writer reads ConnectionStrings:Audit (not Laboratories). Point it at the Testcontainers
        // database, so a test that restores the real ITrustedAuditWriter (see ConfigureTestServices below for the
        // default fake) can read audit rows next to the identity data.
        builder.UseSetting("ConnectionStrings:Audit", TestDatabaseFixture.ConnectionString);

        builder.ConfigureServices(services =>
        {
            // Pronađi i ukloni postojeću registraciju PlatformIdentityDbContext-a
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<PlatformIdentityDbContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Registruj isti kontekst ali sa Testcontainers konekcijom
            services.AddDbContext<PlatformIdentityDbContext>(options =>
            {
                options.UseNpgsql(TestDatabaseFixture.ConnectionString);
            });

            // Program's AddAudit captures configuration before this callback; Development .env/user secrets
            // can override UseSetting above. Replace the captured context registration as well, so Audit
            // reads cannot escape the shared container. Child ConfigureTestServices can still supply the
            // restricted runtime connection for real-writer/failure scenarios in the same database.
            services.RemoveAll<AuditDbContext>();
            services.RemoveAll<DbContextOptions<AuditDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuditDbContext>>();
            services.AddDbContext<AuditDbContext>(options =>
                options.UseNpgsql(TestDatabaseFixture.ConnectionString,
                    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrustedAuditWriter>();
            services.AddSingleton<ITrustedAuditWriter, SuccessfulAuditWriter>();
        });
    }
}
