using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Access.Integration;
using Platform.Api.Modules.Administration.Api;
using Platform.Api.Services.Identity;

namespace Platform.Api.Modules.Access.Api;

public sealed record AttemptEndpointMetadata;
public sealed record RuntimeAttemptService(AttemptService Value);

public static class AttemptEndpoints
{
    public const string EnabledSetting = "CourseworkAttempts:Enabled";
    public static bool AddRuntimeAttempts(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>(EnabledSetting)) return false;
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Identity")))
            throw new InvalidOperationException("ACC-04 requires an Identity connection.");
        var rawUrl = configuration["CourseworkAttempts:ProviderBaseUrl"];
        var token = configuration["CourseworkAttempts:ProviderToken"];
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !uri.AbsolutePath.EndsWith('/') ||
            string.IsNullOrWhiteSpace(token) || token.Length is < 32 or > 512 || token.Any(char.IsControl))
            throw new InvalidOperationException("ACC-04 requires a fixed HTTPS provider base URL ending in / and an external service token.");
        services.TryAddSingleton<IdentityAdministrationAccounts>();
        services.TryAddSingleton<AdministrationSessions>();
        services.AddHttpClient<HttpAttemptStore>(client =>
        {
            client.BaseAddress = uri;
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddSingleton<SharedMembershipAdapter>();
        services.AddSingleton<RuntimeDraftPolicy>();
        services.AddAccessAction<RuntimeDraftPolicy>(new(AttemptPolicy.Update, ActionScope.Course, "attempt", false));
        // Existing attempt.read / attempt.read.course remain central course-role policies.
        services.AddScoped<RuntimeAttemptService>(sp =>
        {
            var accounts = sp.GetRequiredService<IdentityAdministrationAccounts>();
            var store = sp.GetRequiredService<HttpAttemptStore>();
            return new(new(accounts, store, sp.GetRequiredService<IAccessDecision>(),
                new AttemptRequestAudit(sp.GetRequiredService<ILogger<AttemptRequestAudit>>()), sp.GetRequiredService<TimeProvider>(),
                new AttemptPolicy(accounts, sp.GetRequiredService<SharedMembershipAdapter>(), store)));
        });
        return true;
    }
    public static void AddAttemptResourceResolution(this IServiceCollection services)
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(IResourceDescriptorResolver));
        if (descriptor.Lifetime != ServiceLifetime.Singleton)
            throw new InvalidOperationException("The shared resource resolver must be singleton-safe.");
        services.Remove(descriptor);
        services.AddSingleton(sp => new AttemptResourceResolver.Original(
            (IResourceDescriptorResolver)(descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(sp) ??
                ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!)), descriptor.ImplementationInstance is null));
        services.AddSingleton<IResourceDescriptorResolver>(sp => new AttemptResourceResolver(
            sp.GetRequiredService<AttemptResourceResolver.Original>().Value, sp.GetRequiredService<IServiceScopeFactory>()));
    }
    public static void MapRuntimeAttempts(this WebApplication app)
    {
        var group = app.MapGroup("/api/resource-attempts").WithMetadata(new AttemptEndpointMetadata());
        group.AddEndpointFilter(async (ctx,next) =>
        {
            var h = ctx.HttpContext;
            if (h.Request.Method != "GET" && (h.Request.Headers["X-Coursework-Request"] != "1" ||
                (h.Request.Headers.TryGetValue("Origin",out var origin) && origin != $"{h.Request.Scheme}://{h.Request.Host}")))
                return Results.Json(new { code="INVALID_REQUEST" },statusCode:400);
            return await next(ctx);
        });
        group.MapGet("/{id}", async (Guid id,HttpContext h,RuntimeAttemptService service,AdministrationSessions sessions,CancellationToken ct)
            => await Execute(h,service,sessions,id,null,AttemptPolicy.Read,ct)).RequireAccess(AttemptPolicy.Read,"attempt","id");
        group.MapGet("/{id}/review", async (Guid id,HttpContext h,RuntimeAttemptService service,AdministrationSessions sessions,CancellationToken ct)
            => await Execute(h,service,sessions,id,null,"attempt.read.course",ct)).RequireAccess("attempt.read.course","attempt","id");
        group.MapPut("/{id}/answer", async (Guid id,UpdateDraftRequest input,HttpContext h,RuntimeAttemptService service,AdministrationSessions sessions,CancellationToken ct)
            => await Execute(h,service,sessions,id,input,AttemptPolicy.Update,ct)).RequireAccess(AttemptPolicy.Update,"attempt","id");
    }
    private static async Task<IResult> Execute(HttpContext h,RuntimeAttemptService service,AdministrationSessions sessions,
        Guid id,UpdateDraftRequest? input,string action,CancellationToken ct)
    {
        if (h.Items[AdministrationRequestIdentity.ActorKey] is not Guid actor) return Results.Unauthorized();
        var result = await service.Value.Execute(actor,id,input,h.TraceIdentifier,ct,
            async cancel => (await sessions.ValidateAsync(h.Request.Cookies[SessionCookie.Name],cancel))?.UserId==actor,action);
        return result.Status==200 ? Results.Ok(result.Value) : Results.Json(new
        { code=result.Status switch { 400=>"INVALID_REQUEST",401=>"AUTHENTICATION_REQUIRED",409=>"RESOURCE_CHANGED",_=>"RESOURCE_UNAVAILABLE" } },statusCode:result.Status);
    }
}

public sealed class RuntimeDraftPolicy(IServiceScopeFactory scopes) : IAccessPolicy
{
    public string Id=>"acc04.runtime.draft";
    public string Version=>"v1"; // central Access audit metadata format
    public async ValueTask<bool> EvaluateAsync(AccessRequest request,CancellationToken ct=default)
    {
        await using var scope=scopes.CreateAsyncScope();
        return await new AttemptPolicy(scope.ServiceProvider.GetRequiredService<IdentityAdministrationAccounts>(),
            scope.ServiceProvider.GetRequiredService<SharedMembershipAdapter>(),scope.ServiceProvider.GetRequiredService<HttpAttemptStore>()).EvaluateAsync(request,ct);
    }
}

public sealed class AttemptRequestAudit(ILogger<AttemptRequestAudit> logger) : ISecurityAudit
{
    // Local diagnostic only. ATT v1 writes its mutation audit atomically and returns a receipt.
    public void Write(SecurityEvent entry) => logger.LogInformation("ACC04_REQUEST_AUDIT:{Audit}",System.Text.Json.JsonSerializer.Serialize(entry));
}
