namespace Platform.LabLocalization.Application;

public enum LocalizationAction
{
    AddTranslation = 1,
}

/// <summary>
/// LAB-07's view of the platform's IAccessDecision contract: subject + action + concrete resource.
/// The decision is made on the server and the implementation must deny by default:
/// an unknown actor, action or resource is denied. Only a teacher may add a translation.
/// </summary>
public interface ILocalizationAccess
{
    bool IsAllowed(Guid actorId, LocalizationAction action, Guid resourceId);
}
