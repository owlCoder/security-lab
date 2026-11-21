using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Modules.Identity.SessionLimiting;
using Platform.Audit.Contracts;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Integration;

namespace Platform.Identity.Tests.Helpers;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SessionOptions.Section}:AbsoluteLifetimeMinutes"] = "480",
                [$"{SessionOptions.Section}:ActivityTimeoutMinutes"] = "45",
                [$"{SessionOptions.Section}:MfaPendingTimeoutMinutes"] = "5"
            });
        });
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
