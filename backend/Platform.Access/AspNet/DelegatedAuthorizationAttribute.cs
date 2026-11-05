namespace Platform.Access.AspNet;

/// <summary>
/// Explicit bridge for independently authorized legacy endpoints during migration.
/// The endpoint MUST enforce authentication and authorization before returning data
/// or mutating state; this marker does not grant permission to any action.
/// New endpoints should prefer RequireAccess with a registered policy.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DelegatedAuthorizationAttribute : Attribute
{
}
