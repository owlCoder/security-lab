using Microsoft.AspNetCore.Builder;
using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.AspNet;

public static class AccessEndpointExtensions
{
    public static TBuilder RequireAccess<TBuilder>(
        this TBuilder builder,
        string action,
        string? resourceType = null,
        string? idRouteParam = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!ActionId.IsValid(action))
            throw new ArgumentException("Invalid access action.", nameof(action));

        if ((resourceType is null) != (idRouteParam is null))
            throw new ArgumentException("ResourceType and IdRouteParam must be provided together.");

        if (resourceType is not null && !AccessMetadataRules.IsValidResourceType(resourceType))
            throw new ArgumentException("Invalid resource type.", nameof(resourceType));

        if (idRouteParam is not null && string.IsNullOrWhiteSpace(idRouteParam))
            throw new ArgumentException("Invalid route parameter name.", nameof(idRouteParam));

        builder.WithMetadata(new RequireAccessAttribute(action)
        {
            ResourceType = resourceType,
            IdRouteParam = idRouteParam
        });

        return builder;
    }

    [Obsolete(
        "Migration required: replace this marker with exactly one RequireAccess(action) and register its policy before AddAccessControl(). The Access middleware rejects delegation metadata; it never grants or bypasses authorization.",
        false)]
    public static TBuilder DelegateAuthorization<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new DelegatedAuthorizationAttribute());
        return builder;
    }

    [Obsolete(
        "Migration required: replace this marker with RequireAccess(publicAction) and register an explicit policy for that ActionScope.Public action before AddAccessControl(). The Access middleware rejects public metadata; it never grants or bypasses authorization.",
        false)]
    public static TBuilder AllowPublic<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new PublicEndpointMetadata());
        return builder;
    }
}
