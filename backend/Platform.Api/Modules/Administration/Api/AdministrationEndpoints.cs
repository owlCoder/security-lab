using Npgsql;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Administration.Persistence;
using Platform.Api.Services.Identity;

namespace Platform.Api.Modules.Administration.Api;

public static class AdministrationEndpoints
{
    public const string EnabledSetting = "CourseworkAdministration:Enabled";
    public const string ConnectionName = "CourseworkAdministration";

    public static bool AddPersistentCourseAdministration(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>(EnabledSetting)) return false;
        var connection = configuration.GetConnectionString(ConnectionName);
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(configuration.GetConnectionString("Identity")))
            throw new InvalidOperationException("ADM-08 requires configured Identity and CourseworkAdministration connections.");
        _ = new NpgsqlConnectionStringBuilder(connection);
        _ = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Identity"));
        services.AddSingleton(new AdministrationDatabase(connection));
        services.AddSingleton<IdentityAdministrationAccounts>();
        services.AddSingleton<AdministrationSessions>();
        services.AddSingleton<PersistentAdministrationPolicy>(sp => new(
            sp.GetRequiredService<AdministrationDatabase>(), sp.GetRequiredService<IdentityAdministrationAccounts>()));
        foreach (var action in new[] { AdministrationPolicy.Read, AdministrationPolicy.Rename, AdministrationPolicy.Delegate, AdministrationPolicy.Revoke })
            services.AddAccessAction<PersistentAdministrationPolicy>(new(action, ActionScope.Course, "course", true));
        services.AddScoped<PersistentAdministrationService>(sp => new(connection,
            sp.GetRequiredService<IdentityAdministrationAccounts>(), sp.GetRequiredService<IAccessDecision>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<PersistentAdministrationService>>()));
        return true;
    }

    public static void MapPersistentCourseAdministration(this WebApplication app)
    {
        var group = app.MapGroup("/api/course-administration").WithMetadata(new AdministrationEndpointMetadata());
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.Request.Method != "GET")
            {
                if (http.Request.Headers["X-Coursework-Request"] != "1" ||
                    (http.Request.Headers.TryGetValue("Origin", out var origin) && origin != $"{http.Request.Scheme}://{http.Request.Host}"))
                    return Results.Json(new { code = "INVALID_REQUEST" }, statusCode: 400);
            }
            return await next(context);
        });
        group.MapGet("/{courseId}", async (Guid courseId, HttpContext http, PersistentAdministrationService service, AdministrationSessions sessions, CancellationToken ct)
            => await Execute(http, service, sessions, courseId, AdministrationPolicy.Read, ct))
            .RequireAccess(AdministrationPolicy.Read, "course", "courseId");
        group.MapPut("/{courseId}/settings", async (Guid courseId, RenameCourseRequest input, HttpContext http, PersistentAdministrationService service, AdministrationSessions sessions, CancellationToken ct)
            => await Execute(http, service, sessions, courseId, AdministrationPolicy.Rename, ct, rename: input))
            .RequireAccess(AdministrationPolicy.Rename, "course", "courseId");
        group.MapPost("/{courseId}/delegations", async (Guid courseId, DelegateRequest input, HttpContext http, PersistentAdministrationService service, AdministrationSessions sessions, CancellationToken ct)
            => await Execute(http, service, sessions, courseId, AdministrationPolicy.Delegate, ct, delegation: input))
            .RequireAccess(AdministrationPolicy.Delegate, "course", "courseId");
        group.MapPost("/{courseId}/delegations/{grantId}/revoke", async (Guid courseId, Guid grantId, RevokeRequest input, HttpContext http, PersistentAdministrationService service, AdministrationSessions sessions, CancellationToken ct)
            => await Execute(http, service, sessions, courseId, AdministrationPolicy.Revoke, ct, grantId: grantId, revoke: input))
            .RequireAccess(AdministrationPolicy.Revoke, "course", "courseId");
    }

    private static async Task<IResult> Execute(HttpContext http, PersistentAdministrationService service, AdministrationSessions sessions,
        Guid course, string action, CancellationToken ct, RenameCourseRequest? rename = null, DelegateRequest? delegation = null,
        Guid? grantId = null, RevokeRequest? revoke = null)
    {
        // Never derive authority from Http.User, headers, role strings or a synthetic cookie.
        if (http.Items[AdministrationRequestIdentity.ActorKey] is not Guid actor) return Results.Unauthorized();
        var result = await service.Execute(actor, course, action, http.TraceIdentifier, rename, delegation, grantId, revoke, ct,
            async token => (await sessions.ValidateAsync(http.Request.Cookies[SessionCookie.Name], token))?.UserId == actor);
        return result.Status == 200 ? Results.Ok(result.Value) : Results.Json(new
        {
            code = result.Status switch { 400 => "INVALID_REQUEST", 401 => "AUTHENTICATION_REQUIRED", 409 => "RESOURCE_CHANGED", _ => "RESOURCE_UNAVAILABLE" }
        }, statusCode: result.Status);
    }
}
