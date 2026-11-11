using Team21.Acc.Api;
using Platform.Api.Security.RevisionAuthorization;
using Platform.Api.Security.AccessAudit;
using Platform.Api.Security.AccessPolicies;
using Platform.Api.Modules.Kt1;
using Platform.Api.Modules.RevisionReviews;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.LaboratoryPersistence.Composition;
using Platform.Api.Modules.Laboratories;

using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.AspNet;
using Platform.Api.Development;
using Platform.Api.Endpoints;
using Platform.Api.Endpoints.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Middleware;
using Platform.Api.Modules.Identity.RateLimiting;
using Platform.Api.Services.Identity;
using Platform.Audit.Composition;
using Platform.Authoring.Composition;
using Platform.Authoring.Application;
using Platform.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Persistence;
using Platform.Identity.Services;
using Platform.LabVisibility.Composition;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Policies;
using Platform.LabVisibility.Infrastructure.Persistence;

// Oba modula imaju IAccessDecision, zato koristimo alias.
using IdentityAccessDecision = Platform.Api.Interfaces.Identity.IAccessDecision;
using PlatformAccessDecision = Platform.Access.Contracts.IAccessDecision;
using LabVisibilityCourseRole = Platform.LabVisibility.Contracts.CourseRole;

var builder = WebApplication.CreateBuilder(args);
builder.AddTeam21Acc();
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddLocalEnvFile();
    builder.Configuration.AddUserSecrets<PlatformIdentityDbContext>(optional: true);
}

// Access module

builder.Services.AddCentralAccessAudit();
builder.Services.AddAccessControl();
builder.Services.AddAccessAspNet();
builder.Services.AddRevisionAuthorization();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<
        IAccessSubjectProvider,
        DevelopmentFakeSubjectProvider>();
}

var kt1Enabled = Kt1Endpoints.Enabled(
    builder.Environment,
    builder.Configuration);

if (kt1Enabled)
    builder.Services.AddCourseworkKt1();

// Continue with the rest of your existing registrations...
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
builder.Services.AddAuditPersistence(builder.Configuration.GetConnectionString("Laboratories"));
builder.Services.AddAuthoring();
builder.Services.AddLabVisibility();
builder.Services.AddIdentityRateLimiting(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddLaboratoriesPersistence(
    builder.Configuration.GetConnectionString("Laboratories"));
builder.Services.AddRevisionReviews();
builder.Services.AddRevisionSecurityPersistence(builder.Configuration.GetConnectionString("Laboratories"));

builder.Services
    .AddAccessPolicy<PublicHealthReadPolicy>(AccessActions.PublicInfoRead.Id)
    .AddAccessAction<CreateAccountPolicy>(
        new ActionDefinition(
            CreateAccountPolicy.ActionId,
            ActionScope.Authenticated,
            null,
            false))
    .AddAccessAction<AuthoringAuthorPolicy>(
        new ActionDefinition(
            AuthoringActions.TemplateList,
            ActionScope.Authenticated,
            null,
            false))
    .AddAccessAction<AuthoringAuthorPolicy>(
        new ActionDefinition(
            AuthoringActions.TemplateCopy,
            ActionScope.Course,
            AuthoringActions.TemplateResourceType,
            false))
    .AddAccessAction<ExplicitDenyAccessPolicy>(
        new ActionDefinition(
            LabVisibilityActions.StudentView,
            ActionScope.Authenticated,
            null,
            false))
    .AddAccessAction<ExplicitDenyAccessPolicy>(
        new ActionDefinition(
            LabVisibilityActions.Preview,
            ActionScope.Authenticated,
            null,
            false))
    .AddAccessAction<EvaluatorKeyPolicy>(
        new ActionDefinition(
            LabVisibilityActions.KeyRead,
            ActionScope.Course,
            "lab",
            true))
    .AddAccessAction<RevisionActionPolicy>(
        new ActionDefinition(
            RevisionActions.Create,
            ActionScope.Course,
            "course",
            false))
    .AddAccessAction<RevisionActionPolicy>(
        new ActionDefinition(
            RevisionActions.Read,
            ActionScope.Course,
            "revision",
            false))
    .AddAccessAction<RevisionActionPolicy>(
        new ActionDefinition(
            RevisionActions.Mine,
            ActionScope.Course,
            "revision",
            false))
    .AddAccessAction<RevisionActionPolicy>(
        new ActionDefinition(
            RevisionActions.PendingReviews,
            ActionScope.Course,
            "revision",
            false))
    .AddAccessAction<RevisionActionPolicy>(
        new ActionDefinition(
            RevisionActions.Submit,
            ActionScope.Course,
            "revision",
            false))
    .AddAccessAction<RevisionApprovalPolicy>(
        new ActionDefinition(
            RevisionActions.Approve,
            ActionScope.Course,
            "revision",
            true))
    .AddAccessAction<RevisionActionPolicy>(
        new ActionDefinition(
            RevisionActions.Return,
            ActionScope.Course,
            "revision",
            false))
    .AddAccessAction<RevisionApprovalPolicy>(
        new ActionDefinition(
            RevisionActions.UnlockAccount,
            ActionScope.System,
            null,
            true));

// IDN-03: status naloga se pise u identity.accounts (isti izvor koji cita prijava), u jednoj transakciji sa opozivom sesija i Audit zapisom.
builder.Services.AddScoped<IAccountStatusStore, EfAccountStatusStore>();
builder.Services.AddScoped<ISessionManager, SessionManager>();
builder.Services.AddSingleton<IAuditWriter, LoggerAuditWriter>();
builder.Services.AddSingleton<IAdminDirectory, ConfigAdminDirectory>();
builder.Services.AddSingleton<IdentityAccessDecision, AdminOnlyAccessDecision>();
builder.Services.AddScoped<ChangeAccountStatus>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<ILogoutService, LogoutService>();
builder.Services.AddScoped<ISessionCreator, StoreSessionCreator>();

builder.Services.AddScoped<Platform.Api.Interfaces.Identity.IIdentityContext, SessionIdentityContext>();


builder.Services.AddScoped<LabCaller>();

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
builder.Services.AddRevisionRequestIdentity();
var app = builder.Build();
app.UseRouting();

// AUD-01/AUD-04 (S-10): correlation ID za svaki zahtev; mora biti prvi middleware
app.UseCorrelationContext();
app.UseAccessControl();


// Identity Rate Limiting
app.UseIdentityRateLimiting();
if (kt1Enabled)
{
    app.UseAuthentication();
    // KT1 policy registrations are isolated within AddCourseworkKt1().
    app.MapCourseworkKt1();
}

// IDN-01: politike i endpoint za kreiranje naloga
app.MapAccountEndpoints();

// AUT-06: radnje i politike za sablone laboratorija, pa endpointi
app.MapAuthoringEndpoints();

// AUT-04/AUT-07: radnje i politike za prikaz laboratorije

// Probni nalozi samo za lokalni razvoj
if (app.Environment.IsDevelopment())
{
    // AUT-04/AUT-07 demo kurs: 1111 je nastavnik, 2222 student, 3333 nije na kursu.
    var demoCourse = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    var membership = app.Services.GetRequiredService<InMemoryCourseMembership>();
    membership.Set(demoCourse, Guid.Parse("11111111-1111-1111-1111-111111111111"), LabVisibilityCourseRole.Teacher);
    membership.Set(demoCourse, Guid.Parse("22222222-2222-2222-2222-222222222222"), LabVisibilityCourseRole.Student);

    // AUT-04: demo laboratorije 4444 (objavljena) i 5555 (nacrt); sa ConnectionStrings:LabVisibility idu u PostgreSQL.
    await app.Services.SeedLabVisibilityDemoAsync();

    // US-02 and US-09: migrate and seed the identity database (skipped without a connection string).
    if (await app.ApplyIdentityMigrationsAsync())
    {
        // IDN-03: the Audit record of a status change is written in the identity transaction,
        // so the audit schema must exist in the same database.
        try
        {
            await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(
                builder.Configuration.GetConnectionString(IdentityModule.ConnectionStringName)!);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning("The audit schema was not migrated: {Message}", ex.Message);
        }

        await app.SeedDevelopmentAccountsAsync();
    }
    // LAB-03 drugi demo kurs: 3333 je student samo ovde, ne na prvom kursu.
    // Primer: X-Test-User: 11111111-1111-1111-1111-111111111111:Teacher pravi laboratoriju na prvom kursu.
    var secondDemoCourse = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    membership.Set(secondDemoCourse, Guid.Parse("33333333-3333-3333-3333-333333333333"), LabVisibilityCourseRole.Student);
}

// Health endpoint
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "security-lab-api",
    note = "Initial scaffold; not a production-ready laboratory platform"
}))
.AllowAnonymous()   // IDN-02 SessionCheckMiddleware
.RequireAccess(AccessActions.PublicInfoRead.Id);

app.UseMiddleware<SessionCheckMiddleware>();

app.MapControllers();
app.MapLaboratories();
app.MapTeam21Acc();
app.MapRevisionReviews();

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

        // This DEVELOPMENT-ONLY diagnostic reports the engine's denial as data.
        // It never grants access to a resource; clients receive no secret data.
        return Results.Ok(new {
            outcome = decision.Outcome == AccessOutcome.Allow ? "allow" : "deny",
            denialReason = decision.DenialReason.ToString(),
            decision.CorrelationId
        });
    })
    .RequireAccess(AccessActions.PublicInfoRead.Id);
}

app.Run();

public partial class Program { }
