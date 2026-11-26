using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Platform.Access.Contracts;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Administration;

// Alias je potreban jer Kt1 koristi isti interfejs kao Access modul, 
// a ovim se izbegava konflikt imena prilikom build-a i jasno označava Kt1 zavisnost.
using Kt1MembershipReader = Platform.Api.Modules.Access.IMembershipReader;

namespace Platform.Api.Modules.Kt1;

public static class Kt1Endpoints
{
    public const string Scheme = "Kt1SyntheticSession";
    public static bool Enabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>("Kt1Demo:Enabled");
    public static readonly IReadOnlyDictionary<string, Guid> Personas = new Dictionary<string, Guid>
    {
        ["ana"] = DemoStore.Ana, ["marko"] = DemoStore.Marko, ["teacher"] = DemoStore.Teacher,
        ["foreign-teacher"] = DemoStore.ForeignTeacher, ["blocked"] = DemoStore.Blocked,
        ["assistant"] = DemoStore.Assistant, ["admin"] = DemoStore.Admin,
        ["other-admin"] = DemoStore.OtherAdmin, ["delegate"] = DemoStore.DelegateUser
    };
    public static IServiceCollection AddCourseworkKt1(this IServiceCollection services)
    {
        services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "SecurityLab.CourseworkKt1";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        });
        services.AddSingleton<DemoStore>();
        services.AddSingleton<IAccountReader>(sp => sp.GetRequiredService<DemoStore>());
        services.AddSingleton<Kt1MembershipReader>(sp => sp.GetRequiredService<DemoStore>());
        services.AddSingleton<IAttemptStore>(sp => sp.GetRequiredService<DemoStore>());
        services.AddSingleton<ICourseAdministrationStore>(sp => sp.GetRequiredService<DemoStore>());
        services.AddSingleton<ISecurityAudit>(sp => sp.GetRequiredService<DemoStore>());
        services.AddSingleton<AttemptPolicy>();
        services.AddSingleton<AdministrationPolicy>();
        services.AddSingleton(TimeProvider.System);
        // Register before AddAccessControl freezes the shared catalog. Synthetic actions
        // have their own namespace and never replace real API/ACC-08 policies.
        services.AddKt1AccessActions();
        services.AddSingleton<Kt1PolicyDecision>();
        services.AddScoped<AttemptService>(sp => new AttemptService(
            sp.GetRequiredService<IAccountReader>(), sp.GetRequiredService<IAttemptStore>(),
            sp.GetRequiredService<Kt1PolicyDecision>(), sp.GetRequiredService<ISecurityAudit>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<AdministrationService>(sp => new AdministrationService(
            sp.GetRequiredService<IAccountReader>(), sp.GetRequiredService<ICourseAdministrationStore>(),
            sp.GetRequiredService<Kt1PolicyDecision>(), sp.GetRequiredService<ISecurityAudit>(),
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }
    private sealed class Kt1PolicyDecision(IAccessDecision inner) : IAccessDecision
    {
        public ValueTask<AccessDecision> DecideAsync(
            AccessRequest request, CancellationToken cancellationToken = default)
            => inner.DecideAsync(request with { Action = Kt1AccessActions.Central(request.Action) }, cancellationToken);
    }

    public static void MapCourseworkKt1(this WebApplication app)
    {
        var g = app.MapGroup("/api/kt1").WithMetadata(new Kt1EndpointMetadata());
        g.MapGet("/info", () => new
        {
            mode = "Synthetic Development-only KT1 simulator. No real credentials or student data.",
            requirements = new[] { "ACC-04", "ADM-08" }, personas = Personas.Keys,
            attempts = new[] { DemoStore.AnaAttempt, DemoStore.MarkoAttempt, DemoStore.DraftAttempt, DemoStore.CompletedAttempt, DemoStore.MissingAttempt },
            courses = new[] { DemoStore.Course, DemoStore.OtherCourse }
        }).RequireAccess(Kt1AccessActions.Info);
        g.MapPost("/session/{persona}", async (string persona, HttpContext http) =>
        {
            if (!Personas.TryGetValue(persona, out var id)) return Results.BadRequest(new { code = "INVALID_PERSONA" });
            await http.SignInAsync(Scheme, new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, Scheme)));
            return Results.Ok(new { persona });
        }).RequireAccess(Kt1AccessActions.Session);
        g.MapPost("/logout", async (HttpContext http) => { await http.SignOutAsync(Scheme); return Results.NoContent(); }).RequireAccess(Kt1AccessActions.Logout);
        g.MapGet("/attempts/{id}", async (string id, HttpContext http, AttemptService service, CancellationToken ct) =>
            !ValidId(id, out var parsed) ? BadId() : Public(await service.Execute(Actor(http), parsed, null, http.TraceIdentifier, ct)))
            .RequireAccess(Kt1AccessActions.Central(AttemptPolicy.Read), "attempt", "id");
        g.MapPut("/attempts/{id}/answer", async (string id, UpdateDraftRequest input, HttpContext http, AttemptService service, CancellationToken ct) =>
            !ValidId(id, out var parsed) ? BadId() : Public(await service.Execute(Actor(http), parsed, input, http.TraceIdentifier, ct)))
            .RequireAccess(Kt1AccessActions.Central(AttemptPolicy.Update), "attempt", "id");
        g.MapGet("/courses/{id}/administration", async (string id, HttpContext http, AdministrationService service, CancellationToken ct) =>
            !ValidId(id, out var parsed) ? BadId() : Public(await service.Execute(Actor(http), parsed, AdministrationPolicy.Read, http.TraceIdentifier, ct: ct)))
            .RequireAccess(Kt1AccessActions.Central(AdministrationPolicy.Read), "course", "id");
        g.MapPut("/courses/{id}/settings", async (string id, RenameCourseRequest input, HttpContext http, AdministrationService service, CancellationToken ct) =>
            !ValidId(id, out var parsed) ? BadId() : Public(await service.Execute(Actor(http), parsed, AdministrationPolicy.Rename, http.TraceIdentifier, rename: input, ct: ct)))
            .RequireAccess(Kt1AccessActions.Central(AdministrationPolicy.Rename), "course", "id");
        g.MapPost("/courses/{id}/delegations", async (string id, DelegateRequest input, HttpContext http, AdministrationService service, CancellationToken ct) =>
            !ValidId(id, out var parsed) ? BadId() : Public(await service.Execute(Actor(http), parsed, AdministrationPolicy.Delegate, http.TraceIdentifier, delegation: input, ct: ct)))
            .RequireAccess(Kt1AccessActions.Central(AdministrationPolicy.Delegate), "course", "id");
        g.MapPost("/courses/{id}/delegations/{grantId}/revoke", async (string id, string grantId, RevokeRequest input, HttpContext http, AdministrationService service, CancellationToken ct) =>
            !ValidId(id, out var parsed) || !ValidId(grantId, out var grant) ? BadId() :
                Public(await service.Execute(Actor(http), parsed, AdministrationPolicy.Revoke, http.TraceIdentifier, grantId: grant, revoke: input, ct: ct)))
            .RequireAccess(Kt1AccessActions.Central(AdministrationPolicy.Revoke), "course", "id");
    }
    private static Guid? Actor(HttpContext http) => http.User.Identity?.IsAuthenticated == true &&
        Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private static bool ValidId(string id, out Guid value) => Guid.TryParse(id, out value) && value != Guid.Empty;
    private static IResult BadId() => Results.Json(new { code = "INVALID_REQUEST" }, statusCode: 400);
    private static IResult Public(OperationResult result) => result.Status switch
    {
        200 => Results.Ok(result.Value),
        400 => Results.Json(new { code = "INVALID_REQUEST" }, statusCode: 400),
        401 => Results.Json(new { code = "AUTHENTICATION_REQUIRED" }, statusCode: 401),
        409 => Results.Json(new { code = "RESOURCE_CHANGED" }, statusCode: 409),
        _ => Results.Json(new { code = "RESOURCE_UNAVAILABLE" }, statusCode: 404)
    };
}
