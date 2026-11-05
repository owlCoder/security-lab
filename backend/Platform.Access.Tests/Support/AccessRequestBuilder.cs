using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Tests.Support;

public sealed class AccessRequestBuilder
{
    private SubjectRef? subject = new("subject-1", AccountState.Active);
    private string action = AccessActions.LabRead.Id;
    private ResourceRef? resource = new("laboratory", "res-1");
    private string correlationId = "corr-test-1";

    public static AccessRequestBuilder Default() => new();

    public AccessRequestBuilder WithSubject(SubjectRef? value) { subject = value; return this; }
    public AccessRequestBuilder WithState(AccountState state) { subject = new SubjectRef("subject-1", state); return this; }
    public AccessRequestBuilder Anonymous() { subject = null; return this; }
    public AccessRequestBuilder WithAction(string value) { action = value; return this; }
    public AccessRequestBuilder WithResource(ResourceRef? value) { resource = value; return this; }
    public AccessRequestBuilder WithCorrelationId(string value) { correlationId = value; return this; }

    public AccessRequest Build() => new(subject, action, resource, correlationId);
}
