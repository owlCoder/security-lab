using Microsoft.AspNetCore.Builder;

namespace Platform.Access.AspNet;

/// <summary>
/// Annotates a previously implemented endpoint that enforces its own authorization,
/// so strict ACC-05 routing does not preempt its existing permission checks.
/// Only use after reviewing the actual endpoint's authorization behavior.
/// </summary>
public static class LegacyAuthorizationIntegrationExtensions
{
    public static TBuilder DelegatesToLegacyAuthorization<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new DelegatedAuthorizationAttribute());
        return builder;
    }
}
