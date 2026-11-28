using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Tests.Support;

public sealed class FakeMembershipReader(
    Func<Guid, Guid, CancellationToken, ValueTask<CourseMembership?>>? handler = null) : IMembershipReader
{
    private readonly List<(Guid AccountId, Guid CourseId)> calls = [];

    public IReadOnlyList<(Guid AccountId, Guid CourseId)> Calls
    {
        get { lock (calls) { return calls.ToArray(); } }
    }

    public ValueTask<CourseMembership?> GetAsync(Guid accountId, Guid courseId, CancellationToken cancellationToken = default)
    {
        lock (calls) { calls.Add((accountId, courseId)); }
        return handler is null ? ValueTask.FromResult<CourseMembership?>(null) : handler(accountId, courseId, cancellationToken);
    }
}

public sealed class FakeResourceResolver(
    string resourceType,
    Func<ResourceRef, CancellationToken, ValueTask<ResourceDescriptor?>>? handler = null,
    Func<ResourceRef, SubjectRef, CancellationToken, ValueTask<ResourceDescriptor?>>? subjectHandler = null) : IResourceAttributeResolver
{
    private int calls;
    private readonly List<SubjectRef> subjects = [];

    public string ResourceType => resourceType;

    public int Calls => Volatile.Read(ref calls);

    public IReadOnlyList<SubjectRef> Subjects
    {
        get { lock (subjects) { return subjects.ToArray(); } }
    }

    public ValueTask<ResourceDescriptor?> ResolveAsync(ResourceRef resource, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        return handler is null ? ValueTask.FromResult<ResourceDescriptor?>(null) : handler(resource, cancellationToken);
    }

    public ValueTask<ResourceDescriptor?> ResolveAsync(
        ResourceRef resource,
        SubjectRef subject,
        CancellationToken cancellationToken)
    {
        lock (subjects) { subjects.Add(subject); }

        if (subjectHandler is null)
        {
            return ResolveAsync(resource, cancellationToken);
        }

        Interlocked.Increment(ref calls);
        return subjectHandler(resource, subject, cancellationToken);
    }
}

public sealed class FakeGrantReader(
    Func<Guid, string, CancellationToken, ValueTask<IReadOnlyList<ElevatedGrant>>>? handler = null) : IElevatedGrantReader
{
    private readonly List<(Guid HolderId, string Action)> calls = [];

    public IReadOnlyList<(Guid HolderId, string Action)> Calls
    {
        get { lock (calls) { return calls.ToArray(); } }
    }

    public ValueTask<IReadOnlyList<ElevatedGrant>> GetForHolderAsync(Guid holderId, string action, CancellationToken cancellationToken)
    {
        lock (calls) { calls.Add((holderId, action)); }
        return handler is null
            ? ValueTask.FromResult<IReadOnlyList<ElevatedGrant>>(Array.Empty<ElevatedGrant>())
            : handler(holderId, action, cancellationToken);
    }
}
