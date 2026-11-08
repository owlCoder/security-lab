using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Audit.Composition;
using Platform.Audit.Endpoints;
using Platform.Audit.Tests.Authorization;
using static Platform.Audit.Tests.Authorization.AuditAuthorizationExamples;

namespace Platform.Audit.Tests.CourseView;

/// <summary>
/// The S-14 route template and its Access metadata in front of the real AccessControlMiddleware,
/// real Access engine and real S-08 policy. The handler is a test stand-in (the real one needs the
/// S-07 query service); these tests prove who reaches it, not what it returns.
/// </summary>
[TestFixture]
public sealed class CourseAuditRouteTests
{
    private static readonly Guid CourseM = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    private StubCourseAuditMemberships memberships = null!;
    private ConcurrentQueue<Guid> handled = null!;
    private WebApplication app = null!;
    private HttpClient client = null!;

    [SetUp]
    public async Task StartHost()
    {
        memberships = new StubCourseAuditMemberships();
        handled = new ConcurrentQueue<Guid>();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        builder.Services.AddSingleton<IMembershipReader>(memberships);
        builder.Services.AddAccessControl();
        builder.Services.AddAudit();
        builder.Services.AddScoped<IAccessSubjectProvider, HeaderTestSubjectProvider>();

        app = builder.Build();
        app.UseRouting();
        app.UseCorrelationContext();
        app.UseAccessControl();
        app.MapGet(CourseAuditEndpointRoute.Pattern, (Guid courseId) =>
            {
                handled.Enqueue(courseId);
                return Results.NoContent();
            })
            .RequireCourseAuditRead();

        await app.StartAsync();
        client = app.GetTestClient();
    }

    [TearDown]
    public async Task StopHost()
    {
        client.Dispose();
        await app.DisposeAsync();
    }

    private Task<HttpResponseMessage> Get(Guid course, string? subject) => Get(CourseAuditEndpointRoute.For(course), subject);

    private Task<HttpResponseMessage> Get(string path, string? subject)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (subject is not null)
            request.Headers.Add(HeaderTestSubjectProvider.HeaderName, subject);
        return client.SendAsync(request);
    }

    private static async Task<string> BodyWithoutCorrelation(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return string.Join(";", document.RootElement.EnumerateObject()
            .Where(property => property.Name != "correlationId")
            .Select(property => property.Name + "=" + property.Value.GetRawText()));
    }

    [Test]
    public async Task Anonymous_Gets401_WithoutReachingTheHandler()
    {
        memberships.Set(Membership(CourseRole.Teacher));

        using var response = await Get(CourseK, subject: null);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(handled, Is.Empty);
        });
    }

    [Test]
    public async Task TeacherOfK_ReachesTheHandler_ForK()
    {
        memberships.Set(Membership(CourseRole.Teacher));

        using var response = await Get(CourseK, TeacherId.ToString("D"));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(handled, Is.EqualTo(new[] { CourseK }));
        });
    }

    [Test]
    public async Task TeacherOfK_Gets403_ForL_AndTheDecisionUsesTheRouteCourse()
    {
        memberships.Set(Membership(CourseRole.Teacher));
        var asked = new ConcurrentQueue<(Guid Account, Guid Course)>();
        memberships.Handler = (account, course, _) =>
        {
            asked.Enqueue((account, course));
            return ValueTask.FromResult(memberships.Snapshot(account, course));
        };

        using var response = await Get(CourseL, TeacherId.ToString("D"));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(handled, Is.Empty);
            Assert.That(asked, Is.EqualTo(new[] { (TeacherId, CourseL) }));
        });
    }

    [Test]
    public async Task Forbidden_LooksTheSame_ForAnExistingAndAnUnknownCourse()
    {
        // L has another teacher; M has nobody. The caller must not be able to tell them apart.
        memberships.Set(Membership(CourseRole.Teacher));
        memberships.Set(Membership(CourseRole.Teacher, accountId: OtherAccountId, courseId: CourseL));

        using var existing = await Get(CourseL, TeacherId.ToString("D"));
        using var unknown = await Get(CourseM, TeacherId.ToString("D"));

        var existingBody = await BodyWithoutCorrelation(existing);
        var unknownBody = await BodyWithoutCorrelation(unknown);

        Assert.Multiple(() =>
        {
            Assert.That(existing.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(unknown.StatusCode, Is.EqualTo(existing.StatusCode));
            Assert.That(unknownBody, Is.EqualTo(existingBody));
            Assert.That(handled, Is.Empty);
        });
    }

    [Test]
    public async Task BlockedTeacher_Gets403()
    {
        memberships.Set(Membership(CourseRole.Teacher));

        using var response = await Get(CourseK, "blocked:" + TeacherId.ToString("D"));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(handled, Is.Empty);
        });
    }

    [TestCase("/api/courses/not-a-guid/audit-events")]
    [TestCase("/api/courses/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'--/audit-events")]
    public async Task MalformedCourseId_IsNotRouted_AndNoMembershipIsRead(string path)
    {
        using var response = await Get(path, TeacherId.ToString("D"));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(memberships.Calls, Is.Zero);
            Assert.That(handled, Is.Empty);
        });
    }

    [Test]
    public async Task QueryStringCourse_IsIgnored_OnlyTheRouteCourseIsAuthorized()
    {
        // A teacher of K cannot reach L by adding a course filter of their own.
        memberships.Set(Membership(CourseRole.Teacher));

        using var response = await Get($"{CourseAuditEndpointRoute.For(CourseL)}?courseId={CourseK:D}", TeacherId.ToString("D"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public void Endpoint_DeclaresAuditReadOnTheRouteCourse()
    {
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == CourseAuditEndpointRoute.Pattern);
        var requirement = endpoint.Metadata.GetMetadata<IAccessRequirementMetadata>();

        Assert.Multiple(() =>
        {
            Assert.That(requirement, Is.Not.Null);
            Assert.That(requirement!.Action, Is.EqualTo("audit.events.read"));
            Assert.That(requirement.ResourceType, Is.EqualTo("course"));
            Assert.That(requirement.IdRouteParam, Is.EqualTo(CourseAuditEndpointRoute.CourseIdParameter));
            Assert.That(endpoint.Metadata.GetMetadata<PublicEndpointMetadata>(), Is.Null);
            Assert.That(endpoint.Metadata.GetMetadata<DelegatedAuthorizationAttribute>(), Is.Null);
        });
    }
}
