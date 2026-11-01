namespace Platform.Access.Contracts;

public interface IAccessPolicyRegistry
{
    bool TryGet(string action, out IAccessPolicy? policy);
}
