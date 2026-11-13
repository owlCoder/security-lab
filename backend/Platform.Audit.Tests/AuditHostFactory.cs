using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Access.AspNet;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;

namespace Platform.Audit.Tests;

internal sealed class AuditHostFactory(string runtimeConnectionString) : WebApplicationFactory<Program>
{
    // The route carries the synthetic probe course, so the probe can declare RequireAccess (ACC-05).
    internal const string ProbeRoute = "/__test/audit-events/courses/{courseId}";
    internal const string ProbePath = "/__test/audit-events/courses/" + AuditTestAccess.ProbeCourse;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(AuditTestProbeController).Assembly);
            AuditTestAccess.AllowProbeCourse(services);
            services.AddDbContext<AuditDbContext>(options => options.UseNpgsql(runtimeConnectionString));
            services.AddScoped<IAuditEventStore, PostgresAuditEventStore>();
            services.AddScoped<AuditDraftValidator>();
            services.AddScoped<IAuditContextProvider>(_ => new SyntheticAuditContextProvider());
            services.AddScoped<IAuditWriter, AuditWriter>();
        });
    }

    private sealed class SyntheticAuditContextProvider : IAuditContextProvider
    {
        public AuditContext GetCurrentContext() => AuditDraftExamples.UserContext;
    }
}

// Discovered only when AuditHostFactory adds the test assembly as an application part.
[ApiController]
public sealed class AuditTestProbeController : ControllerBase
{
    // Only exists in the Testing application-part; its trusted synthetic context
    // performs the identity binding, with no route in production.
    // ACC-05: a real Access check (audit.events.read on the probe course, S-08 policy, synthetic
    // teacher from AuditTestAccess). AllowAnonymous only skips the IDN-02 session check.
    [AllowAnonymous]
    [RequireAccess("audit.events.read", ResourceType = "course", IdRouteParam = "courseId")]
    [HttpPost(AuditHostFactory.ProbeRoute)]
    public async Task<IActionResult> Write(
        [FromBody] AuditEventDraft draft,
        [FromServices] IAuditWriter writer,
        CancellationToken cancellationToken)
    {
        var result = await writer.WriteAsync(draft, cancellationToken);
        return result.Status switch
        {
            AuditWriteStatus.Recorded => NoContent(),
            AuditWriteStatus.Rejected => BadRequest(),
            _ => StatusCode(503)
        };
    }
}
