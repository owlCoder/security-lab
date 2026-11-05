using Platform.LabLocalization.Application;

namespace Platform.LabLocalization.Tests.Fakes;

/// <summary>Stands in for the revision module: a laboratory has the revision it was given here, or none.</summary>
public sealed class FakeRevisionReader : ILabRevisionReader
{
    private readonly Dictionary<Guid, RevisionInfo> _current = new();

    private readonly Dictionary<(Guid LabId, Guid RevisionId), RevisionInfo> _all = new();

    public void SetCurrent(Guid labId, RevisionInfo revision)
    {
        _current[labId] = revision;
        _all[(labId, revision.RevisionId)] = revision;
    }

    public Task<RevisionInfo?> FindForLabAsync(Guid labId, Guid revisionId, CancellationToken ct = default) =>
        Task.FromResult(_all.GetValueOrDefault((labId, revisionId)));

    public Task<RevisionInfo?> FindCurrentForLabAsync(Guid labId, CancellationToken ct = default) =>
        Task.FromResult(_current.GetValueOrDefault(labId));
}
