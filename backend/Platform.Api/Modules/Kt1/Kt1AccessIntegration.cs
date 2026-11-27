using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Administration;

namespace Platform.Api.Modules.Kt1;

// Present only on routes mapped by the explicitly enabled Development simulator.
public sealed record Kt1EndpointMetadata;

public static class Kt1AccessActions
{
    public const string Info = "kt1.info.read";
    public const string Session = "kt1.session.create";
    public const string Logout = "kt1.session.logout";
    public static string Central(string logicalAction) => $"kt1.{logicalAction}";
    public static readonly IReadOnlyList<string> AttemptActions = [AttemptPolicy.Read, AttemptPolicy.Update];
    public static readonly IReadOnlyList<string> AdministrationActions =
        [AdministrationPolicy.Read, AdministrationPolicy.Rename, AdministrationPolicy.Delegate, AdministrationPolicy.Revoke];

    public static IServiceCollection AddKt1AccessActions(this IServiceCollection services)
    {
        foreach (var action in new[] { Info, Session, Logout })
            services.AddAccessAction<Kt1PublicPolicy>(new(action, ActionScope.Public, null, false));
        foreach (var action in AttemptActions)
            services.AddAccessAction<Kt1ResourcePolicy>(new(Central(action), ActionScope.Course, "attempt", false));
        foreach (var action in AdministrationActions)
            services.AddAccessAction<Kt1ResourcePolicy>(new(Central(action), ActionScope.Course, "course", true));
        return services;
    }
}

// No legacy public marker and no broad permit policy: only these three resource-free
// simulator operations are public. The routes and registrations do not exist in Production.
public sealed class Kt1PublicPolicy : IAccessPolicy
{
    public string Id => "kt1.public";
    public string Version => "v1";
    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(request.Resource is null &&
            request.Action is Kt1AccessActions.Info or Kt1AccessActions.Session or Kt1AccessActions.Logout);
    }
}

// The central engine invokes the existing concrete ownership/membership/delegation
// rules against current server-side state. Real API action policies are untouched.
public sealed class Kt1ResourcePolicy(AttemptPolicy attempts, AdministrationPolicy administration) : IAccessPolicy
{
    public string Id => "kt1.resource";
    public string Version => "acc04:v3+adm08:v1";
    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        foreach (var action in Kt1AccessActions.AttemptActions)
            if (request.Action == Kt1AccessActions.Central(action))
                return attempts.EvaluateAsync(request with { Action = action }, cancellationToken);
        foreach (var action in Kt1AccessActions.AdministrationActions)
            if (request.Action == Kt1AccessActions.Central(action))
                return administration.EvaluateAsync(request with { Action = action }, cancellationToken);
        return ValueTask.FromResult(false);
    }
}

public static class Kt1RequestIdentity
{
    // Decorate after the Identity, laboratory and revision providers. All non-KT1
    // endpoints continue through that entire existing chain.
    public static IServiceCollection AddKt1RequestIdentity(this IServiceCollection services)
    {
        var original = services.LastOrDefault(d => d.ServiceType == typeof(IAccessSubjectProvider))
            ?? throw new InvalidOperationException("Register Access subject providers before KT1 request identity.");
        services.Remove(original);
        services.Add(new ServiceDescriptor(typeof(OriginalSubjectProvider), sp =>
            new OriginalSubjectProvider((IAccessSubjectProvider)(original.ImplementationInstance ??
                original.ImplementationFactory?.Invoke(sp) ??
                ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)),
                original.ImplementationInstance is null), original.Lifetime));
        services.AddScoped<IAccessSubjectProvider>(sp =>
            new Kt1SessionSubjectProvider(sp.GetRequiredService<OriginalSubjectProvider>().Value));
        return services;
    }

    private sealed class OriginalSubjectProvider(IAccessSubjectProvider value, bool ownsValue) : IDisposable
    {
        public IAccessSubjectProvider Value => value;
        public void Dispose() { if (ownsValue) (Value as IDisposable)?.Dispose(); }
    }
}

public sealed class Kt1SessionSubjectProvider(IAccessSubjectProvider inner) : IAccessSubjectProvider
{
    public async ValueTask<SubjectRef?> GetSubjectAsync(HttpContext http, CancellationToken cancellationToken = default)
    {
        if (http.GetEndpoint()?.Metadata.GetMetadata<Kt1EndpointMetadata>() is null)
            return await inner.GetSubjectAsync(http, cancellationToken);

        // Authenticate this scheme explicitly, before the Access middleware evaluates
        // the request. Never promote X-Test-User, X-Stub-User or another module's cookie.
        var authentication = await http.AuthenticateAsync(Kt1Endpoints.Scheme);
        var principal = authentication.Principal;
        if (!authentication.Succeeded || principal?.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity());
            return null;
        }
        http.User = principal;
        // The concrete policy re-reads current account status and grants on every
        // operation; a valid cookie never freezes an old administrative authority.
        return new(actor.ToString("D"), AccountState.Active);
    }
}

/// <summary>
/// Keeps the KT1 public 401/404 contract without weakening central authorization.
/// Resource denials are evaluated and audited by the SAME global IAccessDecision
/// before the generic middleware can serialize its 403. Allowed requests still
/// traverse that middleware and the service's snapshot/current-grant checks.
/// No result is cached and no policy is replaced with an authentication-only allow.
/// </summary>
public sealed class Kt1AccessContractMiddleware(RequestDelegate next, ILogger<Kt1AccessContractMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http, IAccessSubjectProvider subjects, IAccessDecision decisions, IAccessAuditWriter audit, ISecurityAudit securityAudit, TimeProvider clock)
    {
        var endpoint = http.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<Kt1EndpointMetadata>() is null)
        {
            await next(http);
            return;
        }
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Correlation-ID"] = http.TraceIdentifier;
        // No CORS is enabled. The non-simple header and Strict cookie protect mutations.
        if (!HttpMethods.IsGet(http.Request.Method) && http.Request.Headers["X-Kt1-Demo"] != "1")
        {
            await Results.BadRequest(new { code = "INVALID_REQUEST" }).ExecuteAsync(http);
            return;
        }
        var requirements = endpoint.Metadata.GetOrderedMetadata<IAccessRequirementMetadata>();
        // Invalid/ambiguous metadata is left to central fail-closed validation.
        if (requirements.Count != 1 || requirements[0].ResourceType is null)
        {
            await next(http);
            return;
        }
        var requirement = requirements[0];
        var resourceType = requirement.ResourceType!;
        var subject = await subjects.GetSubjectAsync(http, http.RequestAborted);
        if (subject is null)
        {
            // The central middleware returns 401 before either KT1 service runs.
            // Keep that authoritative decision/audit AND record the module's probe.
            // Read only bounded route metadata: no account/resource lookup or body/cookie logging.
            Guid.TryParse(Convert.ToString(http.Request.RouteValues[requirement.IdRouteParam!]), out var target);
            securityAudit.Write(new(clock.GetUtcNow(), http.TraceIdentifier, null,
                requirement.Action, target, resourceType == "course" && target != Guid.Empty ? target : null,
                "DENY", "UNAUTHENTICATED", "v0"));
            await next(http);
            return;
        }
        if (!Guid.TryParse(Convert.ToString(http.Request.RouteValues[requirement.IdRouteParam!]), out var id) || id == Guid.Empty)
        {
            await Results.BadRequest(new { code = "INVALID_REQUEST" }).ExecuteAsync(http);
            return;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var request = new AccessRequest(subject, requirement.Action,
            new(resourceType, id.ToString("D")), http.TraceIdentifier);
        AccessDecision? decision;
        try
        {
            decision = await decisions.DecideAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Never retry a failed decision as an opportunity to admit the operation.
            decision = AccessDecision.Deny(AccessDenialReason.PolicyError, http.TraceIdentifier);
            logger.LogWarning("KT1 central decision failed; denying operation. CorrelationId: {CorrelationId}", http.TraceIdentifier);
            try { await audit.WriteAsync(request, decision, timeout.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception) { logger.LogWarning("KT1 failure audit unavailable. CorrelationId: {CorrelationId}", http.TraceIdentifier); }
        }
        if (decision?.Outcome != AccessOutcome.Allow)
        {
            // The endpoint/service is not entered on denial; keep the module's
            // metadata-only audit as well as the central engine's decision audit.
            securityAudit.Write(new(clock.GetUtcNow(), http.TraceIdentifier, Guid.Parse(subject.Id),
                requirement.Action, id, resourceType == "course" ? id : null,
                "DENY", "ACCESS_DENIED", decision?.PolicyVersion ?? "v0"));
            await Results.Json(new { code = "RESOURCE_UNAVAILABLE" }, statusCode: 404).ExecuteAsync(http);
            return;
        }
        await next(http);
    }
}
