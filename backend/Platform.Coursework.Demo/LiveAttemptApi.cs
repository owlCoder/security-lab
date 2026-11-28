using System.Collections.Frozen;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Membership;
using PortStatus = Platform.Api.Modules.Access.PortStatus;
using Platform.Api.Modules.Access.Integration;

namespace Platform.Coursework.Demo;

/// <summary>Starts the actual API entry point on Kestrel in the isolated demo child.</summary>
internal sealed class LiveAttemptApi(Uri stubAddress, byte[] stubPin, IMembershipReader memberships)
    : WebApplicationFactory<global::Program>
{
    public const string ChildArgument = "--attempt-api-child";

    public static async Task RunAsync()
    {
        // Only the isolated launcher supplies these demonstration fixtures.
        var address = new Uri(Required("CourseworkAttempts__ProviderBaseUrl"));
        var pin = Convert.FromHexString(Required("COURSEWORK_DEMO_ATT_PIN"));
        var course = Guid.Parse(Required("COURSEWORK_DEMO_COURSE"));
        var memberships = new InMemoryMembershipReader();
        foreach (var key in new[] { "COURSEWORK_DEMO_OWNER", "COURSEWORK_DEMO_OTHER_STUDENT" })
            memberships.Set(new CourseMembership(Guid.Parse(Required(key)), course, CourseRole.Student,
                MembershipStatus.Active, Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal)));

        await using var factory = new LiveAttemptApi(address, pin, memberships);
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps()));
        factory.StartServer();

        // Warm the same named HTTP handler and the stub's metadata route before the
        // first access decision has to load them within the normal policy timeout.
        var draftId = Guid.Parse(Required("COURSEWORK_DEMO_DRAFT"));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<HttpAttemptStore>();
            var probe = await store.FindAttemptAsync(draftId);
            if (probe.Status != PortStatus.Ok || probe.Value?.Id != draftId)
            {
                Console.WriteLine($"ATT_DEMO_DIAG metadata_{probe.Status}");
                throw new InvalidOperationException("The demo ATT metadata probe failed.");
            }
        }
        var apiAddress = factory.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        Console.WriteLine($"ATT_DEMO_READY {apiAddress}");
        var stopping = factory.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        try { await Task.Delay(Timeout.InfiniteTimeSpan, stopping); }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseContentRoot(Directory.GetCurrentDirectory());
        builder.ConfigureServices(services =>
        {
            // Only this demo host substitutes the shared membership provider and pins the stub certificate.
            // The API still sends requests through the real HttpAttemptStore and checks real Identity sessions.
            services.Replace(ServiceDescriptor.Singleton(memberships));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddHttpClient<HttpAttemptStore>()
                .ConfigurePrimaryHttpMessageHandler(() => LoopbackTls.CreateHandler(stubAddress, stubPin))
                .AddHttpMessageHandler(() => new AttemptTransportDiagnostics());
        });
    }

    private sealed class AttemptTransportDiagnostics : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            try
            {
                var response = await base.SendAsync(request, ct);
                Console.WriteLine($"ATT_DEMO_DIAG att_http_{(int)response.StatusCode}");
                return response;
            }
            catch (HttpRequestException error)
            {
                Console.WriteLine($"ATT_DEMO_DIAG att_transport_{error.HttpRequestError}");
                throw;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("ATT_DEMO_DIAG att_request_cancelled");
                throw;
            }
        }
    }

    private static string Required(string key) => Environment.GetEnvironmentVariable(key)
        ?? throw new InvalidOperationException("Start the attempt demo using the launcher.");
}
