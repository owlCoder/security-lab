using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Membership;

using LabVisibilityCourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.Tests.LabVisibility;

[TestFixture("Testing")]
[TestFixture("Development")]
public sealed class StudentLabViewTests
{
    private readonly string environment;
    public StudentLabViewTests(string environment)
    {
        this.environment = environment;
    }
    private const string StudentId = "22222222-2222-2222-2222-222222222222";
    private const string OtherUserId = "33333333-3333-3333-3333-333333333333";
    private const string BlockedUserId = "66666666-6666-6666-6666-666666666666";
    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid TeacherId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid PublishedLabId = Guid.NewGuid();
    private static readonly Guid DraftLabId = Guid.NewGuid();
    private static readonly Guid MissingLabId = Guid.NewGuid();

    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;

    [SetUp]
    public void SetUp()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ISessionManager>();
                    services.AddSingleton<ISessionManager, TestSessionManager>();

                    services.RemoveAll<IAccountStatusReader>();
                    services.AddSingleton<IAccountStatusReader, TestAccountStatusReader>();

                    services.RemoveAll<ILabPublicContentReader>();
                    services.AddSingleton<ILabPublicContentReader>(
                        sp => sp.GetRequiredService<InMemoryLabPublicContentReader>());

                    services.RemoveAll<ICourseMembership>();
                    services.AddSingleton<ICourseMembership>(
                        sp => sp.GetRequiredService<InMemoryCourseMembership>());
                });
            });

        client = factory.CreateClient();

        var membership = factory.Services.GetRequiredService<InMemoryCourseMembership>();

        membership.Set(CourseId, Guid.Parse(StudentId), LabVisibilityCourseRole.Student);
        membership.Set(CourseId, Guid.Parse(BlockedUserId), LabVisibilityCourseRole.Student);

        var reader = factory.Services.GetRequiredService<InMemoryLabPublicContentReader>();

        SaveLab(reader, PublishedLabId, LabVisibilityState.Published);
        SaveLab(reader, DraftLabId, LabVisibilityState.Draft);
    }

    [TearDown]
    public void TearDown()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Test]
    public async Task EnrolledStudent_GetsOnlyPublicFields()
    {
        using var response = await SendAsync(PublishedLabId, StudentId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var view = json.RootElement;
        var tasks = view.GetProperty("tasks");
        Assert.That(tasks.GetArrayLength(), Is.EqualTo(1));

        var task = tasks[0];

        Assert.Multiple(() =>
        {
            Assert.That(view.GetProperty("labId").GetGuid(), Is.EqualTo(PublishedLabId));
            Assert.That(view.GetProperty("title").GetString(), Is.EqualTo("Test laboratorija"));
            Assert.That(task.GetProperty("prompt").GetString(), Is.EqualTo("Izaberi opciju."));

            Assert.That(
                view.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "labId", "title", "goal", "tasks" }));

            Assert.That(
                task.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "taskId", "order", "prompt", "options" }));
        });
    }

    [Test]
    public async Task UserOutsideCourse_GetsNotFound()
    {
        using var response = await SendAsync(PublishedLabId, OtherUserId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task EnrolledStudent_RequestingDraft_GetsNotFound()
    {
        using var response = await SendAsync(DraftLabId, StudentId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task EnrolledStudent_RequestingMissingLab_GetsNotFound()
    {
        using var response = await SendAsync(MissingLabId, StudentId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RequestWithoutIdentity_GetsUnauthorized()
    {
        using var response = await SendAsync(PublishedLabId, null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task BlockedCourseMember_GetsNotFound()
    {
        using var response = await SendAsync(PublishedLabId, BlockedUserId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task InvalidSession_GetsUnauthorized()
    {
        using var response = await SendAsync(PublishedLabId, "invalid-session");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task PublishedLab_WithTrailingSlash_GetsPublicView()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/labs/{PublishedLabId:D}/student-view/");

        request.Headers.Add("Cookie", $"{SessionCookie.Name}={StudentId}");

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task<HttpResponseMessage> SendAsync(Guid labId, string? userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/labs/{labId:D}/student-view");

        if (userId is not null)
        {
            request.Headers.Add("Cookie", $"{SessionCookie.Name}={userId}");
        }

        return await client.SendAsync(request);
    }

    private static void SaveLab(InMemoryLabPublicContentReader reader, Guid labId, LabVisibilityState state)
    {
        var task = new PublicTask("task-1", 1, "Izaberi opciju.", new[] { "A", "B" });

        reader.Save(LabPublicContent.Create(labId, CourseId, TeacherId, state, "Test laboratorija", "Provera studentskog prikaza", new[] { task }));
    }

    private sealed class TestAccountStatusReader : IAccountStatusReader
    {
        public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            AccountSnapshot? account = null;

            if (accountId == Guid.Parse(StudentId) || accountId == Guid.Parse(OtherUserId))
            {
                account = new AccountSnapshot(accountId, AccountStatus.Active);
            }
            else if (accountId == Guid.Parse(BlockedUserId))
            {
                account = new AccountSnapshot(accountId, AccountStatus.Blocked);
            }

            return Task.FromResult(account);
        }
    }

    private sealed class TestSessionManager : ISessionManager
    {
        public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default)
        {
            ValidateSession? session = null;
            if (token == StudentId || token == OtherUserId || token == BlockedUserId)
            {
                session = new ValidateSession(Guid.Parse("99999999-9999-9999-9999-999999999999"), Guid.Parse(token), AuthenticationLevel.Active);
            }

            return Task.FromResult(session);
        }

        public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }
    }
}