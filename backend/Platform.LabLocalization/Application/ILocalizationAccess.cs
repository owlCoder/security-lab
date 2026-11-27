namespace Platform.LabLocalization.Application;

public enum LocalizationAction
{
    /// <summary>Add a translation to a revision. The resource is the revision.</summary>
    AddTranslation = 1,

    /// <summary>Read a laboratory's content in a language. The resource is the laboratory.</summary>
    ReadContent = 2,
}

/// <summary>
/// LAB-07's view of the platform's IAccessDecision contract: subject + action + concrete resource.
/// The decision is made on the server and the implementation must deny by default:
/// an unknown actor, action or resource is denied. Only a teacher may add a translation.
/// </summary>
public interface ILocalizationAccess
{
    Task<bool> IsAllowedAsync(Guid actorId, LocalizationAction action, Guid resourceId, CancellationToken ct = default);
}
