using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.AspNet;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = true)]
public sealed class RequireAccessAttribute
    : Attribute, IAccessRequirementMetadata
{
    public string Action { get; }

    public string? ResourceType { get; set; }

    public string? IdRouteParam { get; set; }

    public RequireAccessAttribute(string action)
    {
        if (!ActionId.IsValid(action))
        {
            throw new ArgumentException(
                "Invalid access action.",
                nameof(action));
        }

        Action = action;
    }
}