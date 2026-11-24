using Microsoft.AspNetCore.Builder;

namespace Platform.Access.AspNet;

public static class LegacyAuthorizationIntegrationExtensions
{
    [Obsolete(
        "Migration required: replace this marker with exactly one RequireAccess(action) and register its policy before AddAccessControl(). The Access middleware rejects legacy delegation metadata; it never grants or bypasses authorization.",
        false)]
    public static TBuilder DelegatesToLegacyAuthorization<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new DelegatedAuthorizationAttribute());
        return builder;
    }
}
