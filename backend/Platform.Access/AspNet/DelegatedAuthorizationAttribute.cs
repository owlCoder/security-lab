namespace Platform.Access.AspNet;

[Obsolete(
    "Migration required: replace this metadata with exactly one RequireAccess action and a registered policy. The Access middleware rejects delegation metadata; it never grants or bypasses authorization.",
    false)]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DelegatedAuthorizationAttribute : Attribute
{
}
