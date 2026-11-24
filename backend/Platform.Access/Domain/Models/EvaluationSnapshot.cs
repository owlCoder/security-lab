using Platform.Access.Domain.Enums;

namespace Platform.Access.Domain.Models;

public sealed record EvaluationSnapshot(
    SubjectRef Subject,
    CourseMembership? Membership,
    ResourceDescriptor? Resource,
    IReadOnlyList<ElevatedGrant> Grants,
    DateTimeOffset Now);
