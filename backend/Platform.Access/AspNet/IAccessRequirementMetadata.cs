namespace Platform.Access.AspNet;

public interface IAccessRequirementMetadata
{
    string Action { get; }

    string? ResourceType { get; }

    string? IdRouteParam { get; }
}