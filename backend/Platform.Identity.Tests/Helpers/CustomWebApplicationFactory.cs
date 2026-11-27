using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Audit.Contracts;
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
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrustedAuditWriter>();
            services.AddSingleton<ITrustedAuditWriter, SuccessfulAuditWriter>();
        });
    }
}
