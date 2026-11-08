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
using Platform.AssignmentOverview.Infrastructure;

namespace Platform.MyAssignments.Tests;

// ASN-02: pravi servis i pravi (privremeni) podaci; zamenjena je samo sesija.
[TestFixture]
public class MyAssignmentsWithDataTests
{
    private const string Route = "/api/me/assignments";
    private const string Token = "test-token";

    private static WebApplicationFactory<Program> CreateFactory(string environment, Guid studentId) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager>(new FixedSession(studentId));
            });
        });

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Route);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={Token}");
        return await client.SendAsync(request);
    }

    [Test]
    public async Task EnrolledStudent_GetsOnlyAssignmentsOfOwnCourses()
    {
        using var factory = CreateFactory("Development", Guid.Parse(DevelopmentAssignmentData.StudentId));
        using var client = factory.CreateClient();

        var response = await GetAsync(client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = json.RootElement.GetProperty("assignments").EnumerateArray()
            .Select(a => a.GetProperty("assignmentId").GetGuid())
            .ToArray();

        Assert.That(ids, Is.EquivalentTo(new[]
        {
            DevelopmentAssignmentData.OibAssignmentId,
            DevelopmentAssignmentData.SecurityAssignmentId,
            DevelopmentLockData.LockedAssignmentId,
            DevelopmentLockData.UnlockedAssignmentId
        }));
        Assert.That(ids, Does.Not.Contain(DevelopmentAssignmentData.OtherCourseAssignmentId));
    }

    // ASN-07: dodela sa nepoloženim preduslovom je u odgovoru označena kao zaključana.
    [Test]
    public async Task Response_MarksAssignmentWithUnfinishedPrerequisiteAsLocked()
    {
        using var factory = CreateFactory("Development", Guid.Parse(DevelopmentAssignmentData.StudentId));
        using var client = factory.CreateClient();

        var response = await GetAsync(client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("assignments").EnumerateArray()
            .ToDictionary(a => a.GetProperty("assignmentId").GetGuid());

        var locked = items[DevelopmentLockData.LockedAssignmentId];
        Assert.That(locked.GetProperty("isLocked").GetBoolean(), Is.True);
        Assert.That(
            locked.GetProperty("missingAssignmentIds").EnumerateArray().Select(x => x.GetGuid()),
            Is.EquivalentTo(new[] { DevelopmentAssignmentData.OibAssignmentId }));

        var unlocked = items[DevelopmentLockData.UnlockedAssignmentId];
        Assert.That(unlocked.GetProperty("isLocked").GetBoolean(), Is.False);
    }

    [Test]
    public async Task StudentWithoutEnrollment_GetsEmptyList()
    {
        using var factory = CreateFactory("Development", Guid.NewGuid());
        using var client = factory.CreateClient();

        var response = await GetAsync(client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(json.RootElement.GetProperty("assignments").GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public void OutsideDevelopment_FailsClosed_InsteadOfReturningInventedData()
    {
        using var factory = CreateFactory("Production", Guid.Parse(DevelopmentAssignmentData.StudentId));
        using var client = factory.CreateClient();

        // Test server prosledjuje neuhvacen izuzetak; na pravom serveru je to odgovor 500 bez podataka.
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => GetAsync(client));

        Assert.That(error!.Message, Does.Contain("no implementation outside Development"));
    }

    private sealed class FixedSession(Guid userId) : ISessionManager
    {
        public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default)
            => Task.FromResult<ValidateSession?>(
                token == Token ? new ValidateSession(Guid.NewGuid(), userId, AuthenticationLevel.Active) : null);

        public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SessionInfo>>(Array.Empty<SessionInfo>());
    }
}
