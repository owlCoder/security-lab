using Platform.LabLocalization.Application;

namespace Platform.LabLocalization.Tests.Fakes;

/// <summary>Deny by default, like the real contract: only the listed teachers may add a translation.</summary>
public sealed class FakeLocalizationAccess(params Guid[] teacherIds) : ILocalizationAccess
{
    public bool IsAllowed(Guid actorId, LocalizationAction action, Guid resourceId) =>
        action == LocalizationAction.AddTranslation && teacherIds.Contains(actorId);
}
