using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Services.Identity;

namespace Platform.Api.Modules.Administration.Api;

public sealed record AdministrationEndpointMetadata;

public static class AdministrationRequestIdentity
{
    internal static readonly object ActorKey = new();
    internal static readonly object IdentityUnavailableKey = new();
    public static bool AppliesTo(HttpContext http) => http.GetEndpoint()?.Metadata.GetMetadata<AdministrationEndpointMetadata>() is not null ||
        http.GetEndpoint()?.Metadata.GetMetadata<Platform.Api.Modules.Access.Api.AttemptEndpointMetadata>() is not null;

    public static IServiceCollection AddAdministrationRequestIdentity(this IServiceCollection services)
    {
        var original = services.LastOrDefault(d => d.ServiceType == typeof(IAccessSubjectProvider))
            ?? throw new InvalidOperationException("Register the platform subject provider before ADM-08 identity.");
        services.Remove(original);
        services.Add(new ServiceDescriptor(typeof(OriginalProvider), sp => new OriginalProvider(
            (IAccessSubjectProvider)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(sp) ??
                ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), original.ImplementationInstance is null), original.Lifetime));
        services.AddScoped<IAccessSubjectProvider>(sp => new AdministrationSubjectProvider(
            sp.GetRequiredService<OriginalProvider>().Value, sp.GetRequiredService<AdministrationSessions>(),
            sp.GetRequiredService<ILogger<AdministrationSubjectProvider>>()));
        return services;
    }

    private sealed class OriginalProvider(IAccessSubjectProvider value, bool ownsValue) : IDisposable
    {
        public IAccessSubjectProvider Value => value;
        public void Dispose() { if (ownsValue) (Value as IDisposable)?.Dispose(); }
    }
}

public sealed class AdministrationSubjectProvider(IAccessSubjectProvider inner, AdministrationSessions sessions,
    ILogger<AdministrationSubjectProvider> logger) : IAccessSubjectProvider
{
    public async ValueTask<SubjectRef?> GetSubjectAsync(HttpContext http, CancellationToken cancellationToken = default)
    {
        if (!AdministrationRequestIdentity.AppliesTo(http)) return await inner.GetSubjectAsync(http, cancellationToken);
        http.Items.Remove(AdministrationRequestIdentity.ActorKey);
        try
        {
            var session = await sessions.ValidateAsync(http.Request.Cookies[SessionCookie.Name], cancellationToken);
            if (session is null) return null;
            http.Items[AdministrationRequestIdentity.ActorKey] = session.UserId;
            return new(session.UserId.ToString("D"), AccountState.Active);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is NpgsqlException or DbUpdateException or IOException or TimeoutException)
        {
            http.Items[AdministrationRequestIdentity.IdentityUnavailableKey] = true;
            logger.LogWarning("ADM-08 session provider unavailable. CorrelationId: {CorrelationId}", http.TraceIdentifier);
            return null;
        }
    }
}
