using Microsoft.AspNetCore.Authorization;
using Platform.Api.Modules.Access.Demo;
using Platform.Api.Services.Identity;
using Platform.Identity;
using Platform.Identity.Data;
using Platform.Identity.Domain;

var builder = WebApplication.CreateBuilder(args);
var accessDemoEnabled = AccessDemoEndpoints.Enabled(builder.Environment, builder.Configuration);
if (accessDemoEnabled) builder.Services.AddAccessKt1Demo();


if (builder.Environment.IsDevelopment())
    builder.Configuration.AddUserSecrets<PlatformIdentityDbContext>(optional: true);

builder.Services.AddIdentityModule(builder.Configuration);


builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccountRepository, InMemoryAccountRepository>(); // TODO: zameniti trajnim repozitorijumom (PostgreSQL)
builder.Services.AddSingleton<ISessionManager, NoOpSessionRevoker>();   // zameniti kad IDN-02 bude gotov
builder.Services.AddSingleton<IAuditWriter, LoggerAuditWriter>();
builder.Services.AddSingleton<IAdminDirectory, ConfigAdminDirectory>();
builder.Services.AddSingleton<IAccessDecision, AdminOnlyAccessDecision>();
builder.Services.AddScoped<ChangeAccountStatus>();
builder.Services.AddScoped<LoginAccount>();
builder.Services.AddSingleton<ISessionCreator, UnavailableSessionCreator>(); // zameniti kad US-03 (skladiste sesija) bude gotov

if (builder.Environment.IsDevelopment())
    builder.Services.AddScoped<IIdentityContext, StubIdentityContext>();
else
    builder.Services.AddScoped<IIdentityContext, AnonymousIdentityContext>();

var app = builder.Build();
if (accessDemoEnabled)
{
    app.UseAuthentication();
    app.MapAccessKt1Demo();
}

// Probni nalozi samo za lokalni razvoj (izmišljeni domen example.com).
if (app.Environment.IsDevelopment())
{
    var repo = app.Services.GetRequiredService<IAccountRepository>();
    repo.Save(new UserAccount(Guid.Parse("11111111-1111-1111-1111-111111111111"), "admin@example.com"));
    repo.Save(new UserAccount(Guid.Parse("22222222-2222-2222-2222-222222222222"), "student@example.com"));
    repo.Save(new UserAccount(Guid.Parse("33333333-3333-3333-3333-333333333333"), "other@example.com"));

    // US-09: seed development accounts in the identity database (skipped without a connection string).
    await app.SeedDevelopmentAccountsAsync();
}
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "security-lab-api",
    note = "Initial scaffold; not a production-ready laboratory platform"
})).AllowAnonymous();

app.UseMiddleware<SessionCheckMiddleware>();

app.MapControllers();

app.Run();

public partial class Program { }