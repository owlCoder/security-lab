using Platform.Api.Modules.Kt1;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.LaboratoryPersistence.Composition;
using Platform.Api.Modules.Laboratories;

using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Development;
using Platform.Api.Endpoints.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Middleware;
using Platform.Api.Modules.Identity.RateLimiting;
using Platform.Api.Services.Identity;
using Platform.Audit.Composition;
using Platform.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Persistence;
using Platform.LabVisibility.Composition;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Membership;

// Oba modula imaju IAccessDecision, zato koristimo alias.
using IdentityAccessDecision = Platform.Api.Interfaces.Identity.IAccessDecision;
using PlatformAccessDecision = Platform.Access.Contracts.IAccessDecision;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddLocalEnvFile();
    builder.Configuration.AddUserSecrets<PlatformIdentityDbContext>(optional: true);
}

// Access modul
builder.Services.AddAccessControl();
var kt1Enabled = Kt1Endpoints.Enabled(builder.Environment, builder.Configuration);
if (kt1Enabled) builder.Services.AddCourseworkKt1();

// IDN-01: registracija naloga
builder.Services.AddIdentityModule();

// IDN-02: prijava, odjava i sesije
builder.Services.AddIdentityModule(builder.Configuration);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IRequestSubjectResolver, DevelopmentSubjectResolver>();
}
else
{
    builder.Services.AddSingleton<IRequestSubjectResolver, UnauthenticatedSubjectResolver>();
}

builder.Services.AddAudit();
builder.Services.AddLabVisibility();
builder.Services.AddIdentityRateLimiting(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccountRepository, InMemoryAccountRepository>(); // TODO: zameniti trajnim repozitorijumom (PostgreSQL)
builder.Services.AddScoped<ISessionManager, StoreSessionManager>();   // zameniti kad IDN-02 bude gotov
builder.Services.AddScoped<ISessionManager, SessionManager>();
builder.Services.AddSingleton<IAuditWriter, LoggerAuditWriter>();
builder.Services.AddSingleton<IAdminDirectory, ConfigAdminDirectory>();
builder.Services.AddSingleton<IdentityAccessDecision, AdminOnlyAccessDecision>();
builder.Services.AddScoped<ChangeAccountStatus>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<ILogoutService, LogoutService>();
builder.Services.AddScoped<ISessionCreator, StoreSessionCreator>();
// IDN-02: the caller comes from the server-side session cookie (fully qualified: Laboratories has its own IIdentityContext).
builder.Services.AddScoped<Platform.Api.Interfaces.Identity.IIdentityContext, SessionIdentityContext>();


// Laboratories modul
builder.Services.AddLaboratories(builder.Environment);
// Use the official LAB-01 repository contract. Never silently persist to memory in production.
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Authoring")))
{
    builder.Services.AddLaboratoryPersistence(builder.Configuration);
    builder.Services.AddScoped<ILaboratoryRepository, PostgresLaboratoryRepository>();
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<ILaboratoryRepository, InMemoryLaboratoryRepository>();
}
else
{
    // Keep unrelated API routes available, but never silently write laboratories to memory.
    builder.Services.AddSingleton<ILaboratoryRepository, UnavailableLaboratoryRepository>();
}

// Kreiranje aplikacije
var app = builder.Build();

// Identity Rate Limiting
app.UseIdentityRateLimiting();
if (kt1Enabled)
{
    app.UseAuthentication();
    app.RegisterCourseworkPolicies();
    app.MapCourseworkKt1();
}

// IDN-01: politike i endpoint za kreiranje naloga
app.Services.RegisterIdentityAccess();
app.MapAccountEndpoints();

// AUT-04/AUT-07: radnje i politike za prikaz laboratorije
app.Services.RegisterLabVisibilityAccess();

// Probni nalozi samo za lokalni razvoj
if (app.Environment.IsDevelopment())
{
    var repo = app.Services.GetRequiredService<IAccountRepository>();
    repo.Save(new UserAccount(Guid.Parse("11111111-1111-1111-1111-111111111111"), "admin@example.com"));
    repo.Save(new UserAccount(Guid.Parse("22222222-2222-2222-2222-222222222222"), "student@example.com"));
    repo.Save(new UserAccount(Guid.Parse("33333333-3333-3333-3333-333333333333"), "other@example.com"));

    // AUT-04/AUT-07 demo kurs: 1111 je nastavnik, 2222 student, 3333 nije na kursu.
    var demoCourse = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    var membership = app.Services.GetRequiredService<InMemoryCourseMembership>();
    membership.Set(demoCourse, Guid.Parse("11111111-1111-1111-1111-111111111111"), CourseRole.Teacher);
    membership.Set(demoCourse, Guid.Parse("22222222-2222-2222-2222-222222222222"), CourseRole.Student);

    // US-02 and US-09: migrate and seed the identity database (skipped without a connection string).
    if (await app.ApplyIdentityMigrationsAsync())
        await app.SeedDevelopmentAccountsAsync();
}

// Health endpoint
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "security-lab-api",
    note = "Initial scaffold; not a production-ready laboratory platform"
})).AllowAnonymous();

app.UseMiddleware<SessionCheckMiddleware>();

app.MapControllers();
app.MapLaboratories();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/access/demo-deny", async (
        PlatformAccessDecision accessDecision,
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