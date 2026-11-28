using Microsoft.AspNetCore.Http;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Membership.Tests.Testing;

/// <summary>
/// Test dvojnik: ko je pozivalac za Access middleware (Tim 3). U pravoj aplikaciji subjekat
/// dolazi iz validirane sesije; u testu ga zadajemo, isto kao Identity:DevSubjectId za servis.
/// </summary>
internal sealed class TestAccessSubjectProvider(Guid? callerId) : IAccessSubjectProvider
{
    public ValueTask<SubjectRef?> GetSubjectAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(callerId is { } id
            ? new SubjectRef(id.ToString("D"), AccountState.Active)
            : null);
}
