using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.AssignmentOverview.Contracts;
using Platform.AssignmentOverview.Domain;

namespace Platform.MyAssignments.Tests;

// ASN-02: pravi HTTP tok; zamenjeni su samo ISessionManager (bez baze) i servis.
[TestFixture]
public class MyAssignmentsEndpointTests
{
    private const string Route = "/api/me/assignments";
    private const string Token = "test-token";
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherStudentId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private StubSessions _sessions = null!;
    private RecordingService _service = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _sessions = new StubSessions();
        _service = new RecordingService();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager>(_sessions);
                services.RemoveAll<IMyAssignmentsService>();
                services.AddSingleton<IMyAssignmentsService>(_service);
            });
        });
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private HttpRequestMessage Get(string url, bool withSession = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (withSession)
            request.Headers.Add("Cookie", $"{SessionCookie.Name}={Token}");
        return request;
    }

    [Test]
    public async Task LoggedInStudent_GetsOwnAssignmentsAsJson()
    {
        _sessions.Accept(Token, StudentId, AuthenticationLevel.Active);
        var assignmentId = Guid.NewGuid();
        var due = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);
        _service.Returns(new StudentAssignmentView(assignmentId, "course-1", "rev-1", null, due, 3));

        var response = await _client.SendAsync(Get(Route));
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);

        using var json = JsonDocument.Parse(body);
        var item = json.RootElement.GetProperty("assignments").EnumerateArray().Single();
        Assert.That(item.GetProperty("assignmentId").GetGuid(), Is.EqualTo(assignmentId));
        Assert.That(item.GetProperty("courseId").GetString(), Is.EqualTo("course-1"));
        Assert.That(item.GetProperty("maxAttempts").GetInt32(), Is.EqualTo(3));
        Assert.That(json.RootElement.GetProperty("correlationId").GetString(), Is.Not.Empty);
    }

    [Test]
    public async Task StudentWithoutAssignments_GetsEmptyList()
    {
        _sessions.Accept(Token, StudentId, AuthenticationLevel.Active);

        var response = await _client.SendAsync(Get(Route));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(json.RootElement.GetProperty("assignments").GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public async Task NoSessionCookie_Returns401_AndServiceIsNotCalled()
    {
        var response = await _client.SendAsync(Get(Route, withSession: false));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(_service.Calls, Is.Empty);
    }

    [Test]
    public async Task UnknownOrRevokedSession_Returns401_AndServiceIsNotCalled()
    {
        var response = await _client.SendAsync(Get(Route));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(_service.Calls, Is.Empty);
    }

    [Test]
    public async Task SessionWithPendingMfa_Returns401_AndServiceIsNotCalled()
    {
        _sessions.Accept(Token, StudentId, AuthenticationLevel.MfaPending);

        var response = await _client.SendAsync(Get(Route));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(_service.Calls, Is.Empty);
    }

    [TestCase("?studentId=66666666-6666-6666-6666-666666666666")]
    [TestCase("?userId=66666666-6666-6666-6666-666666666666")]
    public async Task ClientCannotChooseTheStudent_SessionAlwaysWins(string query)
    {
        _sessions.Accept(Token, StudentId, AuthenticationLevel.Active);

        var response = await _client.SendAsync(Get(Route + query));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(_service.Calls, Is.EqualTo(new[] { StudentId.ToString() }));
        Assert.That(_service.Calls, Does.Not.Contain(OtherStudentId.ToString()));
    }

    [Test]
    public async Task ResponseContainsOnlyStudentVisibleFields()
    {
        _sessions.Accept(Token, StudentId, AuthenticationLevel.Active);
        _service.Returns(new StudentAssignmentView(
            Guid.NewGuid(), "course-1", "rev-1", null, DateTimeOffset.UtcNow.AddDays(1), 2));

        var response = await _client.SendAsync(Get(Route));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("assignments").EnumerateArray().Single();

        var names = item.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.That(names, Is.EquivalentTo(new[]
        {
            "assignmentId", "courseId", "publishedLabRevisionId",
            "availableFromUtc", "dueAtUtc", "maxAttempts",
            "isLocked", "missingAssignmentIds"
        }));
    }

    private sealed class StubSessions : ISessionManager
    {
        private readonly Dictionary<string, ValidateSession> _accepted = new();

        public void Accept(string token, Guid userId, AuthenticationLevel level)
            => _accepted[token] = new ValidateSession(Guid.NewGuid(), userId, level);

        public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default)
            => Task.FromResult(_accepted.GetValueOrDefault(token));

        public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SessionInfo>>(Array.Empty<SessionInfo>());
    }

    private sealed class RecordingService : IMyAssignmentsService
    {
        private readonly List<StudentAssignmentView> _result = new();

        public List<string> Calls { get; } = new();

        public void Returns(params StudentAssignmentView[] items) => _result.AddRange(items);

        public ValueTask<IReadOnlyList<StudentAssignmentView>> GetForStudentAsync(
            string studentId, CancellationToken cancellationToken = default)
        {
            Calls.Add(studentId);
            return ValueTask.FromResult<IReadOnlyList<StudentAssignmentView>>(_result.ToList());
        }
    }
}
