namespace Platform.Access.AspNet;


[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DelegatedAuthorizationAttribute : Attribute
{
}
