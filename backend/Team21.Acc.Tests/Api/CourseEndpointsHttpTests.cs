using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Team21.Acc.Api.Access;
using Team21.Acc.Api.Courses;
using Team21.Acc.Api.Services;
using Team21.Acc.Core.Courses;
using Team21.Acc.Core.Interfaces;
using Team21.Acc.Infrastructure.Courses.Data;

namespace Team21.Acc.Tests.Api;

/// <summary>
/// ACC-01 over real HTTP: our routes, the ACC-05 access middleware, the Development course
/// authorization and the service, with SQLite in place of Postgres. Spec 8: positive and
/// negative flows, IDOR and default deny are checked at the API, not only in the service.
/// </summary>
public class CourseEndpointsHttpTests
{
    // Development teacher, see DevelopmentCourseAuthorization
    private static readonly Guid Teacher = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Student = Guid.Parse("10000000-0000-0000-0000-000000000002");

    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private RecordingAuditTrail _audit = null!;

    [SetUp]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _audit = new RecordingAuditTrail();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var services = builder.Services;
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, HeaderCurrentActor>();
        services.AddSingleton<IAuditTrail>(_audit);
        services.AddDbContext<CoursesDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ICoursesDbContext>(provider => provider.GetRequiredService<CoursesDbContext>());

        services.AddScoped<IAccessSubjectProvider, HeaderSubjectProvider>();
        services.AddAccessControl();
        services.AddAccessAspNet();
        services.AddTeam21AccessPolicies();
        services.AddCourses();

        _app = builder.Build();
        _app.UseRouting();
        _app.UseAccessControl();
        _app.MapCourses();

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoursesDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Test]
    public async Task Create_Teacher_Returns201WithLocation()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/courses", Teacher,
            new CreateCourseRequest("oib-101", "Information Security", "Intro"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var course = await response.Content.ReadFromJsonAsync<CourseDto>();

        Assert.Multiple(() =>
        {
            Assert.That(course!.Code, Is.EqualTo("OIB-101"));
            Assert.That(course.Status, Is.EqualTo("Active"));
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/api/courses/{course.Id}"));
            Assert.That(_audit.Entries, Does.Contain(("course.create", course.Id, AuditOutcome.Success)));
        });
    }

    [Test]
    public async Task Create_Anonymous_Returns401()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/courses", null,
            new CreateCourseRequest("ANON-101", "Anonymous", null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await CountCoursesAsync(), Is.Zero);
    }

    [Test]
    public async Task Create_Student_Returns403AndIsAudited()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/courses", Student,
            new CreateCourseRequest("STU-101", "Student course", null));

        var count = await CountCoursesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(count, Is.Zero);
            Assert.That(_audit.Entries, Does.Contain(("course.create", Guid.Empty, AuditOutcome.Denied)));
        });
    }

    [Test]
    public async Task Create_BlockedAccount_IsRefusedBeforeTheService()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/courses", Teacher,
            new CreateCourseRequest("BLK-101", "Blocked", null), AccountState.Blocked);

        Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
        Assert.That(await CountCoursesAsync(), Is.Zero);
    }

    [Test]
    public async Task Create_DuplicateCode_Returns409()
    {
        await CreateCourseAsync("DUP-101");

        var response = await SendAsync(HttpMethod.Post, "/api/courses", Teacher,
            new CreateCourseRequest("dup-101", "Again", null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Create_MissingName_Returns422()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/courses", Teacher,
            new CreateCourseRequest("VAL-101", "  ", null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
    }

    [Test]
    public async Task Create_ScriptInName_IsStoredAsPlainText()
    {
        const string name = "<script>alert(1)</script>";

        var response = await SendAsync(HttpMethod.Post, "/api/courses", Teacher,
            new CreateCourseRequest("XSS-101", name, null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        var course = await response.Content.ReadFromJsonAsync<CourseDto>();
        Assert.That(course!.Name, Is.EqualTo(name));
    }

    [Test]
    public async Task Archive_Teacher_Returns204AndCourseStaysReadable()
    {
        var course = await CreateCourseAsync("ARC-101");

        var archive = await SendAsync(HttpMethod.Post, $"/api/courses/{course.Id}/archive", Teacher);
        var again = await SendAsync(HttpMethod.Post, $"/api/courses/{course.Id}/archive", Teacher);
        var read = await SendAsync(HttpMethod.Get, $"/api/courses/{course.Id}", Student);

        var dto = await read.Content.ReadFromJsonAsync<CourseDto>();

        Assert.Multiple(() =>
        {
            Assert.That(archive.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(dto!.Status, Is.EqualTo("Archived"));
            Assert.That(dto.ArchivedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task Archive_ThenEnrollmentPolicy_RefusesNewEnrollment()
    {
        var course = await CreateCourseAsync("ENR-101");
        Assert.That(await CheckEnrollmentAsync(course.Id), Is.EqualTo(CourseEnrollmentDecision.Open));

        await SendAsync(HttpMethod.Post, $"/api/courses/{course.Id}/archive", Teacher);

        Assert.That(await CheckEnrollmentAsync(course.Id), Is.EqualTo(CourseEnrollmentDecision.Archived));
    }

    [Test]
    public async Task Archive_Student_Returns404SameAsMissingCourse()
    {
        var course = await CreateCourseAsync("IDOR-101");

        var forbidden = await SendAsync(HttpMethod.Post, $"/api/courses/{course.Id}/archive", Student);
        var missing = await SendAsync(HttpMethod.Post, $"/api/courses/{Guid.NewGuid()}/archive", Teacher);

        var forbiddenDetail = await ProblemDetailAsync(forbidden);
        var missingDetail = await ProblemDetailAsync(missing);

        Assert.Multiple(() =>
        {
            Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(forbiddenDetail, Is.EqualTo(missingDetail));
            Assert.That(_audit.Entries, Does.Contain(("course.archive", course.Id, AuditOutcome.Denied)));
        });

        var stillActive = await SendAsync(HttpMethod.Get, $"/api/courses/{course.Id}", Teacher);
        var dto = await stillActive.Content.ReadFromJsonAsync<CourseDto>();
        Assert.That(dto!.Status, Is.EqualTo("Active"));
    }

    [Test]
    public async Task Archive_Anonymous_Returns401()
    {
        var course = await CreateCourseAsync("ANA-101");

        var response = await SendAsync(HttpMethod.Post, $"/api/courses/{course.Id}/archive", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Archive_MalformedId_DoesNotReachTheService()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/courses/not-a-guid/archive", Teacher);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(_audit.Entries, Is.Empty);
    }

    [Test]
    public async Task List_SignedIn_ReturnsCoursesSortedByCodeIncludingArchived()
    {
        await CreateCourseAsync("B-COURSE");
        var a = await CreateCourseAsync("A-COURSE");
        await SendAsync(HttpMethod.Post, $"/api/courses/{a.Id}/archive", Teacher);

        var response = await SendAsync(HttpMethod.Get, "/api/courses", Student);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var list = await response.Content.ReadFromJsonAsync<List<CourseDto>>();
        Assert.That(list!.Select(c => (c.Code, c.Status)),
            Is.EqualTo(new[] { ("A-COURSE", "Archived"), ("B-COURSE", "Active") }));
    }

    [Test]
    public async Task List_Anonymous_Returns401WithoutData()
    {
        await CreateCourseAsync("HID-101");

        var response = await SendAsync(HttpMethod.Get, "/api/courses", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("HID-101"));
    }

    [Test]
    public async Task Get_MissingCourse_Returns404()
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/courses/{Guid.NewGuid()}", Student);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task UndeclaredMethod_IsDeniedByDefault()
    {
        var course = await CreateCourseAsync("DEL-101");

        var response = await SendAsync(HttpMethod.Delete, $"/api/courses/{course.Id}", Teacher);

        // No DELETE route exists; the ACC-05 middleware fails closed instead of answering 405
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await CountCoursesAsync(), Is.EqualTo(1));
    }

    private async Task<CourseDto> CreateCourseAsync(string code)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/courses", Teacher,
            new CreateCourseRequest(code, $"Course {code}", null));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), code);
        return (await response.Content.ReadFromJsonAsync<CourseDto>())!;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        Guid? user,
        object? body = null,
        AccountState state = AccountState.Active)
    {
        using var request = new HttpRequestMessage(method, url);
        if (user is Guid id)
        {
            request.Headers.Add(HeaderCurrentActor.HeaderName, id.ToString());
            request.Headers.Add(HeaderSubjectProvider.StateHeader, state.ToString());
        }

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await _client.SendAsync(request);
    }

    private async Task<int> CountCoursesAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICoursesDbContext>();
        return await db.Courses.IgnoreQueryFilters().CountAsync();
    }

    private async Task<CourseEnrollmentDecision> CheckEnrollmentAsync(Guid courseId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var policy = scope.ServiceProvider.GetRequiredService<ICourseEnrollmentPolicy>();
        return await policy.CheckAsync(courseId);
    }

    private static async Task<string?> ProblemDetailAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        return problem?.Detail;
    }

    /// <summary>Stands in for the IDN session: the X-Test-User header with an account state.</summary>
    private sealed class HeaderSubjectProvider : IAccessSubjectProvider
    {
        public const string StateHeader = "X-Test-State";

        public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
        {
            var headers = httpContext.Request.Headers;
            if (!Guid.TryParse(headers[HeaderCurrentActor.HeaderName], out var id) || id == Guid.Empty)
                return ValueTask.FromResult<SubjectRef?>(null);

            var state = Enum.TryParse<AccountState>(headers[StateHeader], out var parsed)
                ? parsed
                : AccountState.Active;

            return ValueTask.FromResult<SubjectRef?>(new SubjectRef(id.ToString(), state));
        }
    }

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        private readonly List<(string, Guid, AuditOutcome)> _entries = [];

        public IReadOnlyList<(string Action, Guid ResourceId, AuditOutcome Outcome)> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public Task RecordAsync(string action, Guid resourceId, AuditOutcome outcome, CancellationToken ct = default)
        {
            lock (_entries) _entries.Add((action, resourceId, outcome));
            return Task.CompletedTask;
        }
    }
}
