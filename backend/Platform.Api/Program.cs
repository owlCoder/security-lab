using Platform.Api.Modules.Access.Demo;

var builder = WebApplication.CreateBuilder(args);
var accessDemoEnabled = AccessDemoEndpoints.Enabled(builder.Environment, builder.Configuration);
if (accessDemoEnabled) builder.Services.AddAccessKt1Demo();
var app = builder.Build();
if (accessDemoEnabled)
{
    app.UseAuthentication();
    app.MapAccessKt1Demo();
}

// Starter endpoint only. Add domain APIs together with server-side authorization.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "security-lab-api",
    note = "Initial scaffold; not a production-ready laboratory platform"
}));

app.Run();

public partial class Program { }
