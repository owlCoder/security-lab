using Platform.LabLocalization.Application;

namespace Platform.LabLocalization.Tests.Fakes;

/// <summary>Deny by default, like the real contract: only the listed teachers may add a translation.</summary>
public sealed class FakeLocalizationAccess(params Guid[] teacherIds) : ILocalizationAccess
{
    public Task<bool> IsAllowedAsync(Guid actorId, LocalizationAction action, Guid resourceId, CancellationToken ct = default) =>
        Task.FromResult(action == LocalizationAction.AddTranslation && teacherIds.Contains(actorId));
}
