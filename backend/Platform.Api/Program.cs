using Platform.Access.Contracts;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Identity.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAccessControl();
builder.Services.AddIdentityRateLimiting(builder.Configuration);

var app = builder.Build();
app.UseIdentityRateLimiting();

// Starter endpoint only. Add domain APIs together with server-side authorization.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "security-lab-api",
    note = "Initial scaffold; not a production-ready laboratory platform"
}));

if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/access/demo-deny", async (
        IAccessDecision accessDecision,
        CancellationToken cancellationToken) =>
    {
        var decision = await accessDecision.DecideAsync(
            new AccessRequest(
                new SubjectRef("demo-student", AccountState.Active),
                "demo.unknown",
                null,
                Guid.NewGuid().ToString("N")),
            cancellationToken);

        return Results.Ok(new
        {
            decision.Outcome,
            decision.DenialReason,
            decision.CorrelationId
        });
    });
}

app.Run();

public partial class Program { }
