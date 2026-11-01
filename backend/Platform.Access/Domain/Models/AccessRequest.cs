namespace Platform.Access.Domain.Models;

public sealed record AccessRequest(
    SubjectRef? Subject,
    string Action,
    ResourceRef? Resource,
    string CorrelationId);
