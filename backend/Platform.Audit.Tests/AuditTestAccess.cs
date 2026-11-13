using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.AspNet;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Tests.Authorization;

namespace Platform.Audit.Tests;

/// <summary>
/// Test-only Access setup for the Audit probe endpoints. ACC-05 (Team 3) rejects
/// <c>[DelegatedAuthorization]</c>, so every probe declares one real <c>RequireAccess</c> action:
/// <c>audit.events.read</c> on <see cref="ProbeCourseId"/>, decided by the real S-08 policy.
/// Only the inputs are synthetic: a fixed active subject and a teacher membership in the probe
/// course. Nothing here is registered by Platform.Api.
/// </summary>
internal static class AuditTestAccess
{
    public const string ProbeCourse = "7e570000-0000-4000-8000-000000000001";
    public static readonly Guid ProbeCourseId = Guid.Parse(ProbeCourse);

    public static void AllowProbeCourse(IServiceCollection services)
    {
        services.RemoveAll<IAccessSubjectProvider>();
        services.AddScoped<IAccessSubjectProvider, FixedTestSubjectProvider>();

        var memberships = new StubCourseAuditMemberships();
        memberships.Set(AuditAuthorizationExamples.Membership(
            CourseRole.Teacher, accountId: AuditDraftExamples.UserId, courseId: ProbeCourseId));
        services.RemoveAll<IMembershipReader>();
        services.AddSingleton<IMembershipReader>(memberships);
    }

    private sealed class FixedTestSubjectProvider : IAccessSubjectProvider
    {
        public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SubjectRef?>(new SubjectRef(AuditDraftExamples.UserId.ToString("D"), AccountState.Active));
    }
}
