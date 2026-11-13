using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Integration;

namespace Platform.Identity.Tests.Helpers;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class 
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // ACC-05: Access denials are written by ITrustedAuditWriter, whose AuditDbContext reads the
        // "Laboratories" connection string. Point it at the same Testcontainers database, so tests can
        // read access.denied rows next to the identity data.
        builder.UseSetting("ConnectionStrings:Laboratories", TestDatabaseFixture.ConnectionString);

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
        });
    }
}