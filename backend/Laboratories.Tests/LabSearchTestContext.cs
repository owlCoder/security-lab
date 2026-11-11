using Platform.Api.Modules.Laboratories.Application;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Membership;

namespace Laboratories.Tests;

internal static class LabSearchTestContext
{
    public static readonly Guid CourseId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public static InMemoryCourseMembership CreateMembership()
    {
        var membership = new InMemoryCourseMembership();
        foreach (string id in new[] { "marko", "jelena" })
        {
            membership.Set(CourseId, CallerId.From(new Caller(id))!.Value, CourseRole.Teacher);
        }

        return membership;
    }

    private sealed record Caller(string UserId) : IIdentityContext
    {
        public bool IsAuthenticated => true;
        public PlatformRole? Role => PlatformRole.Teacher;
    }
}
