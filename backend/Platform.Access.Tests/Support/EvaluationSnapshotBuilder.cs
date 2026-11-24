using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Tests.Support;

public sealed class EvaluationSnapshotBuilder
{
    private SubjectRef subject = new("subject-1", AccountState.Active);
    private CourseMembership? membership;
    private ResourceDescriptor? resource;
    private IReadOnlyList<ElevatedGrant> grants = Array.Empty<ElevatedGrant>();
    private DateTimeOffset now = DateTimeOffset.UnixEpoch;

    public static EvaluationSnapshotBuilder Default() => new();

    public EvaluationSnapshotBuilder WithSubject(SubjectRef value) { subject = value; return this; }
    public EvaluationSnapshotBuilder WithMembership(CourseMembership? value) { membership = value; return this; }
    public EvaluationSnapshotBuilder WithResource(ResourceDescriptor? value) { resource = value; return this; }
    public EvaluationSnapshotBuilder WithGrants(IReadOnlyList<ElevatedGrant> value) { grants = value; return this; }
    public EvaluationSnapshotBuilder WithNow(DateTimeOffset value) { now = value; return this; }

    public EvaluationSnapshot Build() =>
        new(subject, membership, resource, Array.AsReadOnly(grants.ToArray()), now);
}
