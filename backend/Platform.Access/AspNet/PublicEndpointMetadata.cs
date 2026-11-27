namespace Platform.Access.AspNet;

[Obsolete(
    "Migration required: replace this metadata with RequireAccess(publicAction) and a registered ActionScope.Public policy. The Access middleware rejects public metadata; it never grants or bypasses authorization.",
    false)]
public sealed class PublicEndpointMetadata
{
}