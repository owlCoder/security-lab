using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Audit.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Platform.Api.Security.RevisionAuthorization;

public static class RevisionAuditForwarding
{
    public static IServiceCollection AddRevisionAuditForwarding(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RevisionAuditForwardingMarker)))
            return services;

        var original = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IAccessAuditWriter))
            ?? throw new InvalidOperationException("Register Access before REV-05 authorization.");
        if (original.Lifetime != ServiceLifetime.Singleton)
            throw new InvalidOperationException("The Access audit lifetime changed; review the REV-05 adapter.");

        services.AddHttpContextAccessor();
        services.TryAddScoped<IRevisionAuditContextProvider, RevisionAuditContextProvider>();
        services.AddSingleton<RevisionAuditForwardingMarker>();
        services.Replace(ServiceDescriptor.Singleton<IAccessAuditWriter>(provider =>
        {
            var inner = (IAccessAuditWriter)(original.ImplementationInstance ??
                original.ImplementationFactory?.Invoke(provider) ??
                ActivatorUtilities.CreateInstance(provider, original.ImplementationType!));
            return new RevisionAuditForwarder(inner, provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetService<ILogger<RevisionAuditForwarder>>() ?? NullLogger<RevisionAuditForwarder>.Instance,
                original.ImplementationInstance is null);
        }));
        return services;
    }

    private sealed class RevisionAuditForwardingMarker;
}

/// <summary>
/// Preserves the Access audit pipeline and resolves the public trusted Audit writer
/// per operation. No scoped writer or database is captured by singleton Access.
/// Denials remain denied even if auditing is unavailable, following the Access contract.
/// </summary>
public sealed class RevisionAuditForwarder(IAccessAuditWriter inner, IServiceScopeFactory scopes,
    ILogger<RevisionAuditForwarder> logger, bool ownsInner = false) : IAccessAuditWriter, IDisposable
{
    public async ValueTask WriteAsync(AccessRequest request, AccessDecision decision,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await inner.WriteAsync(request, decision, cancellationToken);
        }
        finally
        {
            if (RevisionAuditEventFactory.CreateDeniedEvent(request, decision) is { } draft)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await using var scope = scopes.CreateAsyncScope();
                try
                {
                    var writer = scope.ServiceProvider.GetService<ITrustedAuditWriter>();
                    if (writer is null)
                    {
                        logger.LogError("REV-05 audit unavailable: trusted writer is not registered. CorrelationId: {CorrelationId}",
                            decision.CorrelationId);
                    }
                    else
                    {
                        var context = scope.ServiceProvider.GetRequiredService<IRevisionAuditContextProvider>().GetContext();
                        var result = await writer.WriteAsync(draft, context, timeout.Token);
                        if (!result.IsRecorded)
                            logger.LogError("REV-05 audit was not recorded. Status: {Status}. CorrelationId: {CorrelationId}",
                                result.Status, context.CorrelationId);
                    }
                }
                catch (Exception)
                {
                    // Never log arbitrary exception text: a provider may include sensitive data.
                    logger.LogError("REV-05 audit forwarding failed. CorrelationId: {CorrelationId}", decision.CorrelationId);
                }
            }
        }
    }

    public void Dispose()
    {
        if (ownsInner) (inner as IDisposable)?.Dispose();
    }
}
