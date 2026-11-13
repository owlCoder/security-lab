using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Middleware;
using Platform.Api.Modules.Team21.Integration;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Team21.Acc.Core.Groups;
using Team21.Acc.Core.Groups.Dtos;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Core.Interfaces;
using Team21.Acc.Infrastructure.Groups.Data;
using AccountStatus = Team21.Acc.Core.Groups.Visibility.AccountStatus;
using CourseRole = Team21.Acc.Core.Groups.Visibility.CourseRole;

namespace Team21.Acc.Tests.Api;

public class SessionCurrentActorHostIntegrationTests
{
    private SessionWorld _world = null!;
    private SessionHostFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _world = new SessionWorld();
        await _world.SeedAsync();
        _factory = new SessionHostFactory(_world);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [Test]
    public void HostRegistersExactlyOneScopedActorAndPreservesPlatformIdentity()
    {
        Assert.That(_factory.ActorRegistrations, Has.Length.EqualTo(1));
        Assert.That(_factory.IdentityRegistrations, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_factory.ActorRegistrations[0].Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
            Assert.That(_factory.ActorRegistrations[0].ImplementationType, Is.EqualTo(typeof(SessionCurrentActor)));
            Assert.That(_factory.IdentityRegistrations[0].Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
            Assert.That(_factory.IdentityRegistrations[0].ImplementationType, Is.EqualTo(typeof(SessionIdentityContext)));
        });

        using var firstScope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();
        var actor = firstScope.ServiceProvider.GetRequiredService<ICurrentActor>();
        Assert.That(actor, Is.TypeOf<SessionCurrentActor>());
        Assert.That(firstScope.ServiceProvider.GetRequiredService<ICurrentActor>(), Is.SameAs(actor));
        Assert.That(secondScope.ServiceProvider.GetRequiredService<ICurrentActor>(), Is.Not.SameAs(actor));
        Assert.That(firstScope.ServiceProvider.GetRequiredService<IIdentityContext>(), Is.TypeOf<SessionIdentityContext>());
    }

    [TestCase("GET", "groups/")]
    [TestCase("POST", "groups/")]
    [TestCase("GET", "material-reference")]
    public async Task EveryTeam21EndpointRequiresSession(string method, string route)
    {
        var path = route == "material-reference"
            ? $"/api/materials/assignment/{_world.MaterialId}"
            : $"/api/courses/{_world.CourseId}/" + route
                .Replace("{group}", _world.GroupId.ToString())
                .Replace("{student}", _world.StudentA.ToString());
        using var request = Request(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = route.EndsWith("members", StringComparison.Ordinal)
                ? JsonContent.Create(new { userId = _world.StudentA })
                : JsonContent.Create(new { name = "Unauthorized group" });

        using var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        await using var db = new GroupsDbContext(_world.GroupOptions);
        Assert.That(await db.Groups.CountAsync(), Is.EqualTo(2));
        Assert.That(await db.GroupMembers.CountAsync(), Is.EqualTo(1));
    }

    [TestCase("synthetic-unknown-session")]
    [TestCase(SessionWorld.ExpiredToken)]
    [TestCase(SessionWorld.RevokedToken)]
    [TestCase(SessionWorld.MfaToken)]
    public async Task InvalidExpiredRevokedOrPendingSessionCannotUseTeacherHeader(string token)
    {
        using var request = Request(HttpMethod.Get, GroupsPath, token);
        request.Headers.Add("X-Test-User", _world.Teacher.ToString());

        using var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("X-Test-User")]
    public async Task IdentityHeaderWithoutCookieCannotAuthenticate(string header)
    {
        using var request = Request(HttpMethod.Get, GroupsPath);
        request.Headers.TryAddWithoutValidation(header, _world.Teacher.ToString());

        using var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ValidatedTeacherReachesRealServiceAndSuppliesCreatedBy()
    {
        using var request = Request(HttpMethod.Post, GroupsPath, SessionWorld.TeacherToken);
        request.Headers.Add("X-Test-User", _world.OtherTeacher.ToString());
        request.Headers.Add("X-Stub-User", _world.OtherTeacher.ToString());
        request.Content = JsonContent.Create(new { name = "Session-created group" });

        using var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await response.Content.ReadFromJsonAsync<GroupDto>();
        Assert.That(created, Is.Not.Null);
        await using var db = new GroupsDbContext(_world.GroupOptions);
        var saved = await db.Groups.SingleAsync(group => group.Id == created!.Id);
        Assert.That(saved.CreatedBy, Is.EqualTo(_world.Teacher));
        Assert.That(saved.CourseId, Is.EqualTo(_world.CourseId));
    }

    [TestCase(SessionWorld.StudentAToken)]
    public async Task ValidSessionDoesNotOverrideCourseRoleAuthorization(string token)
    {
        using var request = Request(HttpMethod.Get, GroupsPath, token);
        request.Headers.Add("X-Test-User", _world.Teacher.ToString());
        request.Headers.Add("X-Stub-User", _world.Teacher.ToString());
        request.Headers.Add("X-User-Id", _world.Teacher.ToString());

        using var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("Private group"));
    }

    [Test]
    public async Task ValidTeacherCannotReadGroupFromAnotherCourseThroughSwappedId()
    {
        using var request = Request(HttpMethod.Get, GroupsPath + _world.OtherGroupId, SessionWorld.TeacherToken);

        using var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task MaterialVisibilityUsesSessionUserDespiteSpoofedGroupMemberHeader()
    {
        var path = $"/api/materials/assignment/{_world.MaterialId}";
        using var allowedRequest = Request(HttpMethod.Get, path, SessionWorld.StudentAToken);
        using var allowed = await _client.SendAsync(allowedRequest);
        Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var deniedRequest = Request(HttpMethod.Get, path, SessionWorld.StudentBToken);
        deniedRequest.Headers.Add("X-Test-User", _world.StudentA.ToString());
        using var denied = await _client.SendAsync(deniedRequest);
        Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task IdentityDoesNotLeakBetweenRequestsOnSameClient()
    {
        foreach (var (token, expected) in new (string?, HttpStatusCode)[]
        {
            (SessionWorld.TeacherToken, HttpStatusCode.OK),
            (SessionWorld.OtherTeacherToken, HttpStatusCode.NotFound),
            (null, HttpStatusCode.Unauthorized),
            (SessionWorld.TeacherToken, HttpStatusCode.OK)
        })
        {
            using var request = Request(HttpMethod.Get, GroupsPath, token);
            using var response = await _client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(expected));
        }
    }

    [TestCase(AccountStatus.Blocked, "groups")]
    [TestCase(AccountStatus.Inactive, "material")]
    public async Task AccountStatus_ChangesAreEnforcedOnNextRequestDespiteValidSession(
        AccountStatus deniedStatus, string resource)
    {
        var isMaterial = resource is "materials" or "material";
        var userId = isMaterial ? _world.StudentA : _world.Teacher;
        var token = isMaterial ? SessionWorld.StudentAToken : SessionWorld.TeacherToken;
        var path = resource switch
        {
            "groups" => GroupsPath,
            "group" => GroupsPath + _world.GroupId,
            "members" => GroupsPath + $"{_world.GroupId}/members",
            "materials" => $"/api/courses/{_world.CourseId}/materials",
            "material" => $"/api/materials/assignment/{_world.MaterialId}",
            _ => throw new ArgumentOutOfRangeException(nameof(resource))
        };
        var protectedValue = resource switch
        {
            "members" => _world.StudentA.ToString(),
            "materials" or "material" => _world.MaterialId.ToString(),
            _ => "Private group"
        };

        using var activeRequest = Request(HttpMethod.Get, path, token);
        using var active = await _client.SendAsync(activeRequest);
        Assert.That(active.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await active.Content.ReadAsStringAsync(), Does.Contain(protectedValue));

        // Only the test reader changes. The IDN account, session and membership stay unchanged.
        _world.SetAccountStatus(userId, deniedStatus);
        await AssertSessionIsStillActive(token, userId);
        using var deniedRequest = Request(HttpMethod.Get, path, token);
        using var denied = await _client.SendAsync(deniedRequest);

        Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        await AssertNoProtectedResourceData(denied);

        _world.SetAccountStatus(userId, AccountStatus.Active);
        using var restoredRequest = Request(HttpMethod.Get, path, token);
        using var restored = await _client.SendAsync(restoredRequest);
        Assert.That(restored.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await restored.Content.ReadAsStringAsync(), Does.Contain(protectedValue));
    }

    [TestCase(AccountStatus.Blocked)]
    public async Task AccountStatus_NonActiveTeacherCannotCreateGroup(AccountStatus status)
    {
        _world.SetAccountStatus(_world.Teacher, status);
        await AssertSessionIsStillActive(SessionWorld.TeacherToken, _world.Teacher);
        using var request = Request(HttpMethod.Post, GroupsPath, SessionWorld.TeacherToken);
        request.Content = JsonContent.Create(new { name = "Status-denied group" });

        using var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        await AssertNoProtectedResourceData(response);
        await using var db = new GroupsDbContext(_world.GroupOptions);
        Assert.That(await db.Groups.CountAsync(), Is.EqualTo(2));
        Assert.That(await db.Groups.AnyAsync(group => group.Name == "Status-denied group"), Is.False);
    }

    private async Task AssertSessionIsStillActive(string token, Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<ISessionManager>().ValidateAsync(token);
        Assert.That(session, Is.Not.Null);
        Assert.That(session!.UserId, Is.EqualTo(userId));
        Assert.That(session.Level, Is.EqualTo(AuthenticationLevel.Active));
    }

    private async Task AssertNoProtectedResourceData(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        foreach (var value in new[]
        {
            "Private group", "Other group", _world.CourseId.ToString(), _world.OtherCourseId.ToString(),
            _world.GroupId.ToString(), _world.OtherGroupId.ToString(), _world.MaterialId.ToString(),
            _world.StudentA.ToString(), _world.Teacher.ToString()
        })
            Assert.That(body, Does.Not.Contain(value));
    }

    [TestCase("/api/courses/{course}/groups/", null, HttpStatusCode.Unauthorized)]
    [TestCase("/api/materials/assignment/{material}", null, HttpStatusCode.Unauthorized)]
    public async Task DevelopmentPathsValidateSessionBeforeActorIsUsed(
        string route, string? token, HttpStatusCode expected)
    {
        // A small Development pipeline verifies the real middleware's former bypass paths.
        // It does not execute Program.cs, its startup migrations or hosted seeders.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            Args = []
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IIdentityContext, SessionIdentityContext>();
        builder.Services.AddScoped<ICurrentActor, SessionCurrentActor>();
        builder.Services.AddScoped(_ => new PlatformIdentityDbContext(_world.IdentityOptions));
        builder.Services.AddScoped<ISessionManager, SessionManager>();
        await using var app = builder.Build();
        app.UseRouting();
        app.UseMiddleware<SessionCheckMiddleware>();
        var endpointExecuted = false;
        app.MapGet(route, (ICurrentActor actor) =>
        {
            endpointExecuted = true;
            return actor.IsAuthenticated ? Results.Ok(actor.UserId) : Results.Unauthorized();
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        var path = route.Replace("{course}", _world.CourseId.ToString())
            .Replace("{material}", _world.MaterialId.ToString());
        using var request = Request(HttpMethod.Get, path, token);
        request.Headers.Add("X-Test-User", _world.OtherTeacher.ToString());

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
        Assert.That(endpointExecuted, Is.EqualTo(expected == HttpStatusCode.OK));
        if (expected == HttpStatusCode.OK)
            Assert.That(await response.Content.ReadFromJsonAsync<Guid>(), Is.EqualTo(_world.Teacher));
    }

    private string GroupsPath => $"/api/courses/{_world.CourseId}/groups/";

    private static HttpRequestMessage Request(HttpMethod method, string path, string? token = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
            request.Headers.Add("Cookie", $"{SessionCookie.Name}={token}");
        return request;
    }

    private sealed class SessionHostFactory(SessionWorld world) : WebApplicationFactory<Program>
    {
        public ServiceDescriptor[] ActorRegistrations { get; private set; } = [];
        public ServiceDescriptor[] IdentityRegistrations { get; private set; } = [];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // The real host runs outside Development, so startup cannot apply migrations.
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                ActorRegistrations = services.Where(service => service.ServiceType == typeof(ICurrentActor)).ToArray();
                IdentityRegistrations = services.Where(service => service.ServiceType == typeof(IIdentityContext)).ToArray();
                // Keep real SessionManager, middleware, identity adapter and Team 21 services.
                // Replace only stores and external account/course reader inputs.
                services.Replace(ServiceDescriptor.Scoped<PlatformIdentityDbContext>(
                    _ => new PlatformIdentityDbContext(world.IdentityOptions)));
                services.Replace(ServiceDescriptor.Scoped<IGroupsDbContext>(
                    _ => new GroupsDbContext(world.GroupOptions)));
                services.Replace(ServiceDescriptor.Singleton<IAccountStatusReader>(world));
                services.Replace(ServiceDescriptor.Singleton<ICourseMembershipReader>(world));
                services.Replace(ServiceDescriptor.Singleton<ICourseStateReader>(world));
            });
        }
    }

    private sealed class SessionWorld : IAccountStatusReader, ICourseMembershipReader, ICourseStateReader
    {
        // Controlled Team 21 status input only; this is not an authoritative IDN repository.
        private readonly Dictionary<Guid, AccountStatus> _accountStatuses = new();

        public const string TeacherToken = "synthetic-teacher-session";
        public const string OtherTeacherToken = "synthetic-other-teacher-session";
        public const string StudentAToken = "synthetic-student-a-session";
        public const string StudentBToken = "synthetic-student-b-session";
        public const string ExpiredToken = "synthetic-expired-session";
        public const string RevokedToken = "synthetic-revoked-session";
        public const string MfaToken = "synthetic-mfa-session";

        public Guid Teacher { get; } = Guid.NewGuid();
        public Guid OtherTeacher { get; } = Guid.NewGuid();
        public Guid StudentA { get; } = Guid.NewGuid();
        public Guid StudentB { get; } = Guid.NewGuid();
        public Guid CourseId { get; } = Guid.NewGuid();
        public Guid OtherCourseId { get; } = Guid.NewGuid();
        public Guid GroupId { get; } = Guid.NewGuid();
        public Guid OtherGroupId { get; } = Guid.NewGuid();
        public Guid MaterialId { get; } = Guid.NewGuid();
        public DbContextOptions<PlatformIdentityDbContext> IdentityOptions { get; } =
            new DbContextOptionsBuilder<PlatformIdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public DbContextOptions<GroupsDbContext> GroupOptions { get; } =
            new DbContextOptionsBuilder<GroupsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        public async Task SeedAsync()
        {
            var now = DateTimeOffset.UtcNow;
            await using (var db = new PlatformIdentityDbContext(IdentityOptions))
            {
                foreach (var user in new[] { Teacher, OtherTeacher, StudentA, StudentB })
                {
                    db.Accounts.Add(new UserAccount(user, $"{user:N}@example.invalid"));
                    _accountStatuses[user] = AccountStatus.Active;
                }
                foreach (var (token, user) in new[]
                {
                    (TeacherToken, Teacher), (OtherTeacherToken, OtherTeacher),
                    (StudentAToken, StudentA), (StudentBToken, StudentB),
                    (ExpiredToken, Teacher), (RevokedToken, Teacher), (MfaToken, Teacher)
                })
                {
                    var session = new Session(Guid.NewGuid(), user,
                        SHA256.HashData(Encoding.UTF8.GetBytes(token)), Guid.NewGuid(),
                        token == MfaToken ? AuthLevel.MfaPending : AuthLevel.Active,
                        now.AddHours(-2), token == ExpiredToken ? now.AddHours(-1) : now.AddHours(1));
                    if (token == RevokedToken)
                        session.Revoke(now.AddMinutes(-1));
                    db.Sessions.Add(session);
                }
                await db.SaveChangesAsync();
            }

            await using var groups = new GroupsDbContext(GroupOptions);
            groups.Groups.AddRange(
                new Group { Id = GroupId, CourseId = CourseId, Name = "Private group", CreatedBy = Teacher },
                new Group { Id = OtherGroupId, CourseId = OtherCourseId, Name = "Other group", CreatedBy = OtherTeacher });
            groups.GroupMembers.Add(new GroupMember { GroupId = GroupId, UserId = StudentA, CreatedBy = Teacher });
            var audience = new MaterialAudience
            {
                CourseId = CourseId, MaterialId = MaterialId, MaterialType = MaterialType.Assignment,
                Visibility = MaterialVisibility.RestrictedToGroups, CreatedBy = Teacher
            };
            groups.MaterialAudiences.Add(audience);
            groups.GroupMaterialAccesses.Add(new GroupMaterialAccess
            {
                GroupId = GroupId, MaterialAudienceId = audience.Id, CreatedBy = Teacher
            });
            await groups.SaveChangesAsync();
        }

        public void SetAccountStatus(Guid accountId, AccountStatus status) => _accountStatuses[accountId] = status;

        public Task<AccountStatus> GetStatusAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult(_accountStatuses.GetValueOrDefault(accountId, AccountStatus.Inactive));

        public Task<CourseMembership?> FindAsync(Guid accountId, Guid courseId, CancellationToken ct = default)
        {
            CourseRole? role = (accountId == Teacher && courseId == CourseId)
                || (accountId == OtherTeacher && courseId == OtherCourseId) ? CourseRole.Teacher
                : (accountId == StudentA || accountId == StudentB) && courseId == CourseId ? CourseRole.Student : null;
            return Task.FromResult(role is { } found
                ? new CourseMembership(courseId, found, Active: true, CanReview: false) : null);
        }

        public Task<CourseState?> GetStateAsync(Guid courseId, CancellationToken ct = default) =>
            Task.FromResult<CourseState?>(courseId == CourseId || courseId == OtherCourseId ? CourseState.Active : null);
    }
}
