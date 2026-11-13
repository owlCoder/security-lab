
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using Platform.Membership.Tests.Testing;
using Platform.Membership.Infrastructure.Courses;
using Platform.Access.Domain.Enums;
using Platform.Membership.Contracts;
using Platform.Access.AspNet;

namespace Platform.Membership.Tests.Application;

[TestFixture]
public sealed class MembershipEndpointAuthorizationTests
{
    private static readonly Guid CourseId = DevelopmentCourses.SecurityLab;

    private static readonly Guid StudentId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid OtherStudentId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class ApiFactory(
        string environment,
        Guid? callerId,
        InMemoryMembershipStore store,
        Guid? adminId = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Identity:DevSubjectId"] = callerId?.ToString(),
            ["Identity:AdminAccountIds:0"] = adminId?.ToString(),
                        ["ConnectionStrings:Membership"] = ""
                    });
            });

            builder.ConfigureTestServices(services =>
        {
                services.RemoveAll<IMembershipStore>();
                services.AddSingleton<IMembershipStore>(store);


            // Isti pozivalac za Access middleware (RequireAccess) i za servis.
            services.RemoveAll<IAccessSubjectProvider>();
            services.AddSingleton<IAccessSubjectProvider>(new TestAccessSubjectProvider(callerId));

            services.RemoveAll<IAccountStatusReader>();
                services.AddSingleton<IAccountStatusReader>(
                   new FakeAccountStatusReader()
                        .With(StudentId, AccountState.Active)
                        .With(OtherStudentId, AccountState.Active));
        });
        }
    }

    [Test]
    public async Task Unauthenticated_request_returns_401()
    {
        var store = new InMemoryMembershipStore();

        using var factory = new ApiFactory(
            "Production", null, store);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/courses/{CourseId}/members",
            new { accountId = StudentId });

        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));

        Assert.That(store.All, Is.Empty);
    }

    [Test]
    public async Task Student_request_returns_403()
    {
        var store = new InMemoryMembershipStore();
        store.Seed(CourseId, StudentId, MemberRole.Student);

        using var factory = new ApiFactory(
            "Development", StudentId, store);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/courses/{CourseId}/members",
            new { accountId = OtherStudentId });

        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));

        Assert.That(store.All, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Spoofed_enrolledBy_does_not_grant_permission()
    {
        var store = new InMemoryMembershipStore();
        store.Seed(CourseId, StudentId, MemberRole.Student);

        using var factory = new ApiFactory(
            "Development", StudentId, store);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/courses/{CourseId}/members",
            new
            {
                accountId = OtherStudentId,
                enrolledBy = Guid.Parse(
                    "11111111-1111-1111-1111-111111111111")
            });

        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));

        Assert.That(store.All, Has.Count.EqualTo(1));
    }
    // AUD-04: telo odgovora nosi isti correlation ID kao zaglavlje X-Correlation-ID.
    [Test]
    public async Task Enrollment_uses_the_request_correlation_id()
    {
        var teacherId = Guid.NewGuid();
        var store = new InMemoryMembershipStore();
        store.Seed(CourseId, teacherId, MemberRole.Teacher);

        using var factory = new ApiFactory("Development", teacherId, store);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/courses/{CourseId}/members",
            new { accountId = StudentId });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var header = response.Headers.GetValues("X-Correlation-ID").Single();
        var body = await response.Content.ReadFromJsonAsync<CorrelationBody>();
        Assert.That(body!.CorrelationId, Is.EqualTo(header));
    }

    private sealed record CorrelationBody(string CorrelationId);

    [Test]
public async Task Teacher_of_own_course_can_enroll_student()
{
    var teacherId = Guid.NewGuid();
    var store = new InMemoryMembershipStore();

    store.Seed(CourseId, teacherId, MemberRole.Teacher);

    using var factory = new ApiFactory(
        "Development", teacherId, store);

    using var client = factory.CreateClient();

    var response = await client.PostAsJsonAsync(
        $"/api/courses/{CourseId}/members",
        new { accountId = StudentId });

    Assert.That(response.StatusCode,
        Is.EqualTo(HttpStatusCode.Created));

    Assert.That(store.All, Has.Count.EqualTo(2));

    var studentMembership = store.All.Single(
        m => m.AccountId == StudentId);

    Assert.Multiple(() =>
    {
        Assert.That(studentMembership.CourseId,
            Is.EqualTo(CourseId));

        Assert.That(studentMembership.Role,
            Is.EqualTo(MemberRole.Student));

        Assert.That(studentMembership.Status,
            Is.EqualTo(MemberStatus.Active));

        Assert.That(studentMembership.EnrolledBy,
            Is.EqualTo(teacherId));
    });
}


[Test]
public async Task Teacher_of_another_course_is_forbidden()
{
    var teacherId = Guid.NewGuid();
    var store = new InMemoryMembershipStore();

    store.Seed(Guid.NewGuid(), teacherId, MemberRole.Teacher);

    using var factory = new ApiFactory(
        "Development", teacherId, store);

    using var client = factory.CreateClient();

    var response = await client.PostAsJsonAsync(
        $"/api/courses/{CourseId}/members",
        new { accountId = StudentId });

    Assert.That(response.StatusCode,
        Is.EqualTo(HttpStatusCode.Forbidden));
}


[Test]
public async Task Administrator_can_enroll_student()
{
    var adminId = Guid.NewGuid();
    var store = new InMemoryMembershipStore();

    using var factory = new ApiFactory(
        "Development", adminId, store, adminId);

    using var client = factory.CreateClient();

    var response = await client.PostAsJsonAsync(
        $"/api/courses/{CourseId}/members",
        new { accountId = StudentId });

    Assert.That(response.StatusCode,
        Is.EqualTo(HttpStatusCode.Created));

    Assert.That(store.All, Has.Count.EqualTo(1));

    var membership = store.All.Single();

    Assert.Multiple(() =>
    {
        Assert.That(membership.CourseId,
            Is.EqualTo(CourseId));

        Assert.That(membership.AccountId,
            Is.EqualTo(StudentId));

        Assert.That(membership.Role,
            Is.EqualTo(MemberRole.Student));

        Assert.That(membership.EnrolledBy,
            Is.EqualTo(adminId));
    });
}


}
