using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Platform.Api.Modules.Access.Demo;

public sealed class CookieIdentityContext(IHttpContextAccessor accessor) : IIdentityContext
{
    public Guid? AccountId => accessor.HttpContext?.User.Identity?.IsAuthenticated == true &&
        Guid.TryParse(accessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id : null;
}

public static class AccessDemoEndpoints
{
    public const string Scheme = "AccessKt1Demo";
    public static bool Enabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>("AccessDemo:Enabled");

    private static readonly Dictionary<string, Guid> Personas = new()
    {
        ["ana"] = DemoData.Ana, ["marko"] = DemoData.Marko, ["teacher"] = DemoData.Teacher,
        ["foreign-teacher"] = DemoData.ForeignTeacher, ["blocked"] = DemoData.Blocked,
        ["assistant"] = DemoData.Assistant, ["admin"] = DemoData.Admin
    };

    public static IServiceCollection AddAccessKt1Demo(this IServiceCollection services)
    {
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "SecurityLab.AccessKt1";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        services.AddHttpContextAccessor();
        services.AddScoped<IIdentityContext, CookieIdentityContext>();
        services.AddSingleton<DemoData>();
        services.AddSingleton<IAccountReader>(sp => sp.GetRequiredService<DemoData>());
        services.AddSingleton<IMembershipReader>(sp => sp.GetRequiredService<DemoData>());
        services.AddSingleton<IAttemptReader>(sp => sp.GetRequiredService<DemoData>());
        services.AddSingleton<IAttemptDraftWriter>(sp => sp.GetRequiredService<DemoData>());
        services.AddSingleton<IContextPolicyReader>(sp => sp.GetRequiredService<DemoData>());
        services.AddSingleton<IAuditWriter>(sp => sp.GetRequiredService<DemoData>());
        services.AddSingleton<IAccessDecision, AccessPolicy>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AttemptReadService>();
        services.AddScoped<AttemptDraftService>();
        return services;
    }

    public static void MapAccessKt1Demo(this WebApplication app)
    {
        var group = app.MapGroup("/api/access-demo").AllowAnonymous();
        group.MapGet("/info", () => Results.Ok(new
        {
            mode = "KT1 synthetic fixtures - not production authentication",
            personas = Personas.Keys,
            attempts = new[]
            {
                new { id = DemoData.AnaAttempt, label = "Anin predati pokusaj" },
                new { id = DemoData.MarkoAttempt, label = "Markov predati pokusaj" },
                new { id = DemoData.ClosedAttempt, label = "Anin pokusaj - istekao kontekst" },
                new { id = DemoData.DraftAttempt, label = "Anin nepredati pokusaj" },
                new { id = DemoData.CompletedAttempt, label = "Anin zavrseni pokusaj" },
                new { id = DemoData.ClosedDraftAttempt, label = "Anin nacrt - istekao kontekst" },
                new { id = DemoData.MissingAttempt, label = "Nepostojeci testni ID" }
            }
        }));
        group.MapPost("/session/{persona}", async (string persona, HttpContext http) =>
        {
            // Custom non-simple header + no CORS + Strict cookie blocks cross-site form login.
            if (http.Request.Headers["X-Access-Demo"] != "1") return Results.BadRequest();
            if (!Personas.TryGetValue(persona, out var id)) return Results.BadRequest();
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, Scheme));
            await http.SignInAsync(Scheme, principal);
            return Results.Ok(new { persona });
        });
        group.MapPost("/logout", async (HttpContext http) =>
        {
            if (http.Request.Headers["X-Access-Demo"] != "1") return Results.BadRequest();
            await http.SignOutAsync(Scheme);
            return Results.NoContent();
        });
        group.MapGet("/attempts/{id}", (string id, IIdentityContext identity,
            AttemptReadService service, HttpContext http) =>
        {
            if (!Guid.TryParse(id, out var parsed) || parsed == Guid.Empty)
                return Results.BadRequest(new { code = "INVALID_RESOURCE_ID" });
            var correlation = Guid.NewGuid();
            http.Response.Headers["X-Correlation-ID"] = correlation.ToString();
            http.Response.Headers.CacheControl = "no-store";
            var result = service.Read(identity.AccountId, parsed, correlation);
            return PublicResponse(result.StatusCode, result.Resource);
        });
        group.MapPut("/attempts/{id}/answer", (string id, UpdateDraftRequest request,
            IIdentityContext identity, AttemptDraftService service, HttpContext http) =>
        {
            if (http.Request.Headers["X-Access-Demo"] != "1") return Results.BadRequest();
            if (!Guid.TryParse(id, out var parsed) || parsed == Guid.Empty)
                return Results.BadRequest(new { code = "INVALID_RESOURCE_ID" });
            var correlation = Guid.NewGuid();
            http.Response.Headers["X-Correlation-ID"] = correlation.ToString();
            http.Response.Headers.CacheControl = "no-store";
            var result = service.Update(identity.AccountId, parsed, request, correlation);
            return PublicResponse(result.StatusCode, result.Resource);
        });
    }

    private static IResult PublicResponse(int status, AttemptView? resource) => status switch
    {
        200 => Results.Ok(resource),
        400 => Results.Json(new { code = "INVALID_REQUEST" }, statusCode: 400),
        401 => Results.Json(new { code = "AUTHENTICATION_REQUIRED" }, statusCode: 401),
        409 => Results.Json(new { code = "RESOURCE_CHANGED", message = "Pokusaj je izmenjen. Ponovo ga ucitajte." }, statusCode: 409),
        _ => Results.Json(new { code = "RESOURCE_UNAVAILABLE", message = "Resurs nije dostupan." }, statusCode: 404)
    };
}
