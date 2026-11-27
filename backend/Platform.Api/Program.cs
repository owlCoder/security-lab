using Microsoft.AspNetCore.Antiforgery;
using Team21.Acc.Api;
using Platform.Api.Security.RevisionAuthorization;
using Platform.Api.Security.AccessAudit;
using Platform.Api.Security.AccessPolicies;
using Platform.Api.Security.IdentityAccess;
using Platform.Api.Modules.Kt1;
using Platform.Api.Modules.Competencies;
using Platform.Api.Modules.Administration.Api;

using Platform.Api.Modules.Access.Api;
using Platform.Api.Modules.RevisionReviews;
using Platform.Api.Modules.SessionSecurity;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Infrastructure;

using Platform.LaboratoryPersistence.Composition;
using Platform.Api.Modules.Laboratories;
using Platform.Api.Modules.LabLocalization;
using Platform.Api.Modules.Identity.Federation;
using Platform.Access.Application.Options;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Authorization;
using Platform.Api.Development;
using Platform.Api.Endpoints;
using Platform.Api.Endpoints.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Middleware;
using Platform.Api.Modules.Identity.RateLimiting;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.Api.Modules.RevisionReviews;
using Platform.Api.Services.Identity;
using Platform.Api.Services.Audit;
using Platform.Audit.Application;
using Platform.Audit.Composition;
using Platform.Authoring.Composition;
using Platform.Competencies.Composition;
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
using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Infrastructure;

// Oba modula imaju IAccessDecision, zato koristimo alias.
using IdentityAccessDecision = Platform.Api.Interfaces.Identity.IAccessDecision;
using LabVisibilityCourseRole = Platform.LabVisibility.Contracts.CourseRole;
using PlatformAccessDecision = Platform.Access.Contracts.IAccessDecision;

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

var attemptsEnabled = builder.Services.AddRuntimeAttempts(builder.Configuration);
builder.Services.AddCentralAccessAudit();
builder.Services.AddRevisionAuthorization();
builder.Services.AddRevisionAccessPolicies();
builder.Services.AddAccessAspNet();
builder.Services.AddScoped<IAccessSubjectProvider, ResolverAccessSubjectProvider>();

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

builder.Services.AddAudit(builder.Configuration);
builder.Services.AddScoped<IAuditContextProvider, ServerAuditContextProvider>();

// PRG-04 uses its own schema, but can share the already configured PostgreSQL server.
// A dedicated Competencies connection takes precedence when deployment supplies one.
var competenciesConnectionString = builder.Configuration.GetConnectionString("Competencies");
if (string.IsNullOrWhiteSpace(competenciesConnectionString))
    competenciesConnectionString = builder.Configuration.GetConnectionString("Laboratories");

if (!string.IsNullOrWhiteSpace(competenciesConnectionString))
    builder.Services.AddCompetencyPersistence(competenciesConnectionString);

builder.Services.AddAuthoring();
builder.Services.AddLabVisibility();
builder.Services.AddIdentityRateLimiting(builder.Configuration);
builder.Services.AddControllers();
// US-15 / SEC-03: antiforgery makes and checks the CSRF token pair. AuthController writes the cookie itself with
// Secure always on (CsrfToken.Options), so SameAsRequest only keeps antiforgery from refusing plain HTTP in dev and tests.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = CsrfToken.HeaderName;
    options.Cookie.Name = CsrfToken.CookieName;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    // The token is read only from X-CSRF-TOKEN. The check runs before the ADM-08 body size limit and every other
    // module limit, so antiforgery must never read (buffer) a form body to look for the token there.
    options.SuppressReadingTokenFromFormBody = true;
});
builder.Services.AddSingleton<IAntiforgeryAdditionalDataProvider, CsrfSessionBinding>();
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
            false));

// AUT-04 / AUT-07 / #185: LabVisibility radnje i politike, pre AddAccessControl() (strogi ACC-05).
builder.Services.AddLabVisibilityAccess();

// IDN-03: status naloga se pise u identity.accounts (isti izvor koji cita prijava), u jednoj transakciji sa opozivom sesija i Audit zapisom.
builder.Services.AddScoped<IAccountStatusStore, EfAccountStatusStore>();
builder.Services.AddScoped<ISessionManager, SessionManager>();
builder.Services.AddSingleton<IAuditWriter, LoggerAuditWriter>();
builder.Services.AddSingleton<IAdminDirectory, ConfigAdminDirectory>();
builder.Services.AddSingleton<IdentityAccessDecision, AdminOnlyAccessDecision>();
builder.Services.AddScoped<ChangeAccountStatus>();
// IDN-05: administrator unlock (account.unlock, registered by REV-05), same store and transaction as IDN-03.
builder.Services.AddScoped<UnlockAccount>();
builder.Services.AddScoped<ILoginService, LoginService>();
// IDN-08: login via an external OIDC identity
builder.Services.AddScoped<IFederatedLoginService, FederatedLoginService>();
var oidcEnabled = builder.Services.AddOidcFederation(builder.Configuration, builder.Environment);
builder.Services.AddScoped<ILogoutService, LogoutService>();
builder.Services.AddScoped<ISessionCreator, StoreSessionCreator>();
builder.Services.AddScoped<IdentityAuditProducer>();

// IDN-03: politika i subjekat iz sesije za centralni ACC-05 middleware (pre AddAccessControl).
builder.Services.AddIdentityAuthenticationAccess();
builder.Services.AddIdentitySessionAccess();
builder.Services.AddSessionSecurity();

builder.Services.AddScoped<Platform.Api.Interfaces.Identity.IIdentityContext, SessionIdentityContext>();


builder.Services.AddScoped<LabCaller>();

// Laboratories modul
builder.Services.AddLaboratories(builder.Environment);
builder.Services.AddCatalog(builder.Configuration);
// Use the official LAB-01 repository contract. Never silently persist to memory in production.
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Authoring")))
{
    builder.Services.AddLaboratoryPersistence(builder.Configuration);
    builder.Services.AddScoped<ILaboratoryRepository, PostgresLaboratoryRepository>();
    builder.Services.AddScoped<IPrerequisiteRepository, LabPrerequisiteStoreAdapter>();
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<ILaboratoryRepository, InMemoryLaboratoryRepository>();
    builder.Services.AddSingleton<IPrerequisiteRepository, InMemoryPrerequisiteRepository>();
}
else
{
    // Keep unrelated API routes available, but never silently write laboratories to memory.
    builder.Services.AddSingleton<ILaboratoryRepository, UnavailableLaboratoryRepository>();
    builder.Services.AddSingleton<IPrerequisiteRepository, UnavailablePrerequisiteRepository>();
}

// LAB-07: lokalizacija laboratorija
builder.Services.AddLabLocalizationModule();

// Kreiranje aplikacije
var administrationEnabled = builder.Services.AddPersistentCourseAdministration(builder.Configuration);
builder.Services.AddSingleton(new AccessConfigurationOptions
{
    RequirePolicyCoverage = true,
    ActionsWithoutPolicy = new HashSet<string>(StringComparer.Ordinal)
    {
        AccessActions.PublicCatalogRead.Id,
        AccessActions.SystemConfigManage.Id,
        AccessActions.ResultsSensitiveRead.Id,
        AccessActions.EvaluationTaskProcess.Id
    }
});
builder.Services.AddPrg04CompetencyOperations();

// LAB-06: registracija akcija i politike autorizacije
builder.Services.AddAccessAction<PrerequisiteModificationPolicy>(
    new ActionDefinition(
        "prerequisites.add",
        ActionScope.Course,
        "laboratory",
        true));

builder.Services.AddAccessAction<PrerequisiteModificationPolicy>(
    new ActionDefinition(
        "prerequisites.remove",
        ActionScope.Course,
        "laboratory",
        true));

builder.Services.AddAccessAction<PrerequisiteModificationPolicy>(
    new ActionDefinition(
        "prerequisites.read",
        ActionScope.Course,
        "laboratory",
        true));

builder.Services.AddAccessControl();
builder.Services.AddRevisionRequestIdentity();
if (kt1Enabled) builder.Services.AddKt1RequestIdentity();
if (attemptsEnabled) builder.Services.AddAttemptResourceResolution();

//AUT-06: register the resolver that identifies the caller from the validated session
builder.Services.AddKeyedScoped<IRequestSubjectResolver, AuthoringSessionSubjectResolver>("AuthoringSession");
AuthoringAccessSubjectProvider.Register(builder.Services);
if (administrationEnabled || attemptsEnabled) builder.Services.AddAdministrationRequestIdentity();
builder.Services.AddPrg04RequestIdentity();

var app = builder.Build();
app.UseRouting();

// AUD-01/AUD-04 (S-10): correlation ID za svaki zahtev; mora biti prvi middleware
app.UseCorrelationContext();
// US-15 / SEC-03: right after routing and correlation, before session checks, Access, rate limiting, audit and
// authentication, so a refused request has no side effect (US-14: the session is not extended). The token is bound
// to the session cookie (CsrfSessionBinding), not to HttpContext.User.
app.UseMiddleware<CsrfProtectionMiddleware>();
if (administrationEnabled || attemptsEnabled) app.UseMiddleware<AdministrationResponseMiddleware>();
// Preserve KT1's non-enumerating public denial contract using the same central decider.
if (kt1Enabled) app.UseMiddleware<Kt1AccessContractMiddleware>();
// Identity and catalog routes retain their invalid-session response before ACC-05 checks access.
app.UseWhen(
    context => IdentitySessionSubjectProvider.IsIdentityRoute(context) || CatalogModule.RequiresSession(context),
    routes => routes.UseMiddleware<SessionCheckMiddleware>());
// AUT-06: Authoring uses the validated session to identify the caller.
app.UseWhen(AuthoringAccessSubjectProvider.AppliesTo, authoring => authoring.UseMiddleware<SessionCheckMiddleware>());

app.UseAccessControl();
if (administrationEnabled || attemptsEnabled) app.UseMiddleware<AdministrationBodyLimitMiddleware>();


// Identity Rate Limiting
app.UseIdentityRateLimiting();

// IDN-08: the OIDC handler processes /api/auth/oidc/callback here. KT1 adds the same middleware itself.
if (oidcEnabled && !kt1Enabled)
    app.UseAuthentication();

if (kt1Enabled)
{
    app.UseAuthentication();
    // Only the explicitly enabled simulator maps these namespaced Access actions.
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
    await app.Services.SeedLaboratorySearchDemoAsync();

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

// Downstream handlers read the validated session from HttpContext.Items.
app.UseWhen(
    context => !AuthoringAccessSubjectProvider.AppliesTo(context)
                && !AdministrationRequestIdentity.AppliesTo(context),
    otherRoutes => otherRoutes.UseMiddleware<SessionCheckMiddleware>());

app.MapControllers();
if (administrationEnabled) app.MapPersistentCourseAdministration();
if (attemptsEnabled) app.MapRuntimeAttempts();
app.MapLaboratories();
app.MapLabLocalizationModule();
app.MapCompetencyEndpoints();
app.MapCatalog(app.Configuration);
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
