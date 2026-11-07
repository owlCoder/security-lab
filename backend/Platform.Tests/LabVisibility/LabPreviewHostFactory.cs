using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Tests.LabVisibility;

// AUT-07: pravi API u memoriji (Development), ceo HTTP pipeline kao u aplikaciji.
// Jedina razlika: identitet se čita iz X-Stub-User umesto iz sesije, da test može
// da glumi demo naloge 1111 (nastavnik), 2222 (student) i 3333 (van kursa).
internal sealed class LabPreviewHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIdentityContext>();
            services.AddScoped<IIdentityContext, StubIdentityContext>();
        });
    }
}
