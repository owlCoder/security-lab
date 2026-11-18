using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Access.Infrastructure.Policies;
using Platform.Api.Endpoints;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Modules.Competencies;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Services.Identity;
using Platform.Competencies.Contracts;
using Platform.Competencies.Domain;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Membership;
using AccountStatus = Platform.Identity.Enums.AccountStatus;

namespace Platform.Competencies.Tests;

// Real HTTP host, session adapter and Access decision engine. Only external stores
// are replaced by deterministic test adapters; no production database is used.
[TestFixture]
[NonParallelizable]
public sealed class CompetencyEndpointsTests
{
    private static readonly Guid Teacher = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Student = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid OtherTeacher = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid ForeignTeacher = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid Blocked = Guid.Parse("10000000-0000-0000-0000-000000000005");
    private static readonly Guid UnknownAccount = Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static readonly Guid Course = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid ForeignCourse = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid LabId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherLabId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid ForeignLabId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid LegacyLabId = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid MissingLabId = Guid.Parse("30000000-0000-0000-0000-000000000005");
    private static readonly Guid CompetencyId = BuiltInCompetencies.AccessControlId;
    private static readonly Guid SecondCompetencyId = BuiltInCompetencies.LogAnalysisId;

    [TestCase("GET", null)]
    [TestCase("GET", "revoked")]
    [TestCase("PUT", null)]
    [TestCase("DELETE", null)]
    [TestCase("PUT", "pending")]
    [TestCase("PUT", "blocked")]
    [TestCase("PUT", "empty-user")]
    [TestCase("PUT", "empty-handle")]
    [TestCase("PUT", "unknown-account")]
    public async Task UnauthenticatedRequest_DeniesBeforeCompetencyStorage(string method, string? token)
    {
        using var factory = new Prg04Factory();
        using var client = factory.CreateClient();
        using var response = await Send(client, method, method == "GET" ? Links(LabId) : Link(LabId), token);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Store.ReadCalls, Is.Zero);
        Assert.That(factory.Store.MutationCalls, Is.Zero);
        Assert.That(factory.Catalog.ReadCalls, Is.Zero);
    }

    [TestCase("GET")]
    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task DevelopmentHeadersWithoutSession_DoNotAuthenticatePrg04(string method)
    {
        using var factory = new Prg04Factory("Development");
        using var client = factory.CreateClient();
        using var request = Request(method, method == "GET" ? Links(LabId) : Link(LabId), token: null);
        request.Headers.Add("X-Test-User", $"{Teacher:D}:Teacher");
        request.Headers.Add("X-Stub-User", Teacher.ToString("D"));
        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Store.ReadCalls, Is.Zero);
        Assert.That(factory.Store.MutationCalls, Is.Zero);
        Assert.That(factory.Catalog.ReadCalls, Is.Zero);
    }

    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task MissingPrg04AccessPolicy_DeniesEvenTheAuthorizedTeacher(string method)
    {
        using var factory = new Prg04Factory(hideManagePolicy: true);
        factory.Store.Seed(LabId, CompetencyId);
        using var client = factory.CreateClient();
        using var response = await Send(client, method, Link(LabId), "teacher");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Store.MutationCalls, Is.Zero);
        Assert.That(factory.Catalog.ReadCalls, Is.Zero);
        Assert.That(factory.Store.HasLink(LabId, CompetencyId), Is.True);
    }

    [TestCase("Production")]
    [TestCase("Development")]
    public async Task AuthenticatedTeacher_CanReadCatalogAndOwnLaboratoryLinks(string environment)
    {
        using var factory = new Prg04Factory(environment);
        factory.Store.Seed(LabId, CompetencyId);
        using var client = factory.CreateClient();
        using var catalogResponse = await Send(client, "GET", "/api/competencies", "teacher");
        using var linksResponse = await Send(client, "GET", Links(LabId), "teacher");

        Assert.That(catalogResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await catalogResponse.Content.ReadFromJsonAsync<CompetencyCatalogResponse>())!.Competencies.Count,
            Is.EqualTo(BuiltInCompetencies.All.Count));
        Assert.That(linksResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var links = await linksResponse.Content.ReadFromJsonAsync<LaboratoryCompetenciesResponse>();
        Assert.That(links!.LaboratoryId, Is.EqualTo(LabId));
        Assert.That(links.LaboratoryTitle, Is.EqualTo("Own lab"));
        Assert.That(links.CanManage, Is.True);
        Assert.That(links.Competencies.Select(x => x.Id), Is.EqualTo(new[] { CompetencyId }));
    }

    [TestCase("Production")]
    [TestCase("Development")]
    public async Task AuthorizedTeacher_PutAndDeleteAreIdempotentAndPreserveOtherLinks(string environment)
    {
        using var factory = new Prg04Factory(environment);
        factory.Store.Seed(LabId, SecondCompetencyId);
        factory.Store.Seed(ForeignLabId, CompetencyId);
        using var client = factory.CreateClient();

        using var first = await Send(client, "PUT", Link(LabId), "teacher");
        using var repeat = await Send(client, "PUT", Link(LabId), "teacher");
        using var delete = await Send(client, "DELETE", Link(LabId), "teacher");
        using var repeatedDelete = await Send(client, "DELETE", Link(LabId), "teacher");

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(first.Headers.Location?.OriginalString, Is.EqualTo(Link(LabId)));
        Assert.That(repeat.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(repeatedDelete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(factory.Store.HasLink(LabId, CompetencyId), Is.False);
        Assert.That(factory.Store.HasLink(LabId, SecondCompetencyId), Is.True);
        Assert.That(factory.Store.HasLink(ForeignLabId, CompetencyId), Is.True);
    }

    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task StudentWithReadAccess_DoesNotGetEditAccess(string method)
    {
        using var factory = new Prg04Factory();
        factory.Store.Seed(LabId, CompetencyId);
        using var client = factory.CreateClient();
        using var read = await Send(client, "GET", Links(LabId), "student");
        using var denied = await Send(client, method, Link(LabId), "student");

        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var links = await read.Content.ReadFromJsonAsync<LaboratoryCompetenciesResponse>();
        Assert.That(links!.LaboratoryTitle, Is.EqualTo("Own lab"));
        Assert.That(links.CanManage, Is.False);
        Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Store.MutationCalls, Is.Zero);
        Assert.That(factory.Store.HasLink(LabId, CompetencyId), Is.True);
    }

    [Test]
    public async Task MissingManagePolicy_ReadsLinksWithReadOnlyCapabilityWithoutProbingMutation()
    {
        using var factory = new Prg04Factory(hideManagePolicy: true);
        factory.Store.Seed(LabId, CompetencyId);
        using var client = factory.CreateClient();
        using var response = await Send(client, "GET", Links(LabId), "teacher");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var links = await response.Content.ReadFromJsonAsync<LaboratoryCompetenciesResponse>();
        Assert.That(links!.LaboratoryTitle, Is.EqualTo("Own lab"));
        Assert.That(links.CanManage, Is.False);
        Assert.That(links.Competencies.Select(x => x.Id), Is.EqualTo(new[] { CompetencyId }));
        Assert.That(factory.Store.MutationCalls, Is.Zero);
        Assert.That(factory.Catalog.ReadCalls, Is.Zero);
    }

    [Test]
    public async Task ManageCapability_ReflectsMembershipChangesAndNeverAuthorizesTheNextMutation()
    {
        using var factory = new Prg04Factory();
        factory.Store.Seed(LabId, CompetencyId);
        using var client = factory.CreateClient();
        using var firstRead = await Send(client, "GET", Links(LabId), "teacher");
        Assert.That(firstRead.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await firstRead.Content.ReadFromJsonAsync<LaboratoryCompetenciesResponse>())!.CanManage, Is.True);

        factory.Membership.Set(Course, Teacher, CourseRole.Student);
        using var denied = await Send(client, "DELETE", Link(LabId), "teacher");
        using var nextRead = await Send(client, "GET", Links(LabId), "teacher");

        Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(nextRead.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await nextRead.Content.ReadFromJsonAsync<LaboratoryCompetenciesResponse>())!.CanManage, Is.False);
        Assert.That(factory.Store.MutationCalls, Is.Zero);
        Assert.That(factory.Store.HasLink(LabId, CompetencyId), Is.True);
    }

    [Test]
    public async Task TwoCompetencies_CanBeAddedReadAndRemovedIndependentlyThroughHttp()
    {
        using var factory = new Prg04Factory();
        using var client = factory.CreateClient();
        using var first = await Send(client, "PUT", Link(LabId), "teacher");
        using var second = await Send(client, "PUT", Link(LabId, SecondCompetencyId), "teacher");
        using var read = await Send(client, "GET", Links(LabId), "student");

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var links = await read.Content.ReadFromJsonAsync<LaboratoryCompetenciesResponse>();
        Assert.That(links!.Competencies.Select(x => x.Id),
            Is.EquivalentTo(new[] { CompetencyId, SecondCompetencyId }));

        using var delete = await Send(client, "DELETE", Link(LabId), "teacher");
        using var remaining = await Send(client, "GET", Links(LabId), "student");
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(remaining.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var remainingLinks = await remaining.Content.ReadFromJsonAsync<LaboratoryCompetenciesResponse>();
        Assert.That(remainingLinks!.Competencies.Select(x => x.Id), Is.EqualTo(new[] { SecondCompetencyId }));
    }

    [TestCase("draft")]
    [TestCase("archived")]
    [TestCase("missing")]
    [TestCase("foreign-course")]
    [TestCase("wrong-laboratory")]
    [TestCase("archived-laboratory")]
    public async Task StudentCannotReadLinksWithoutMatchingPublishedLaboratory(string scenario)
    {
        using var factory = new Prg04Factory();
        factory.Store.Seed(LabId, CompetencyId);
        factory.PublicContent.Content = scenario == "missing" ? null : LabPublicContent.Create(
            scenario == "wrong-laboratory" ? OtherLabId : LabId,
            scenario == "foreign-course" ? ForeignCourse : Course, Teacher,
            scenario == "draft" ? LabVisibilityState.Draft :
            scenario == "archived" ? LabVisibilityState.Archived : LabVisibilityState.Published,
            "Lab", "Goal", []);
        if (scenario == "archived-laboratory")
            await factory.Laboratories.UpdateAsync(Laboratory.Rehydrate(
                LabId, Course, "Archived lab", LabStatus.Archived, Teacher, DateTimeOffset.UtcNow));
        using var client = factory.CreateClient();
        using var response = await Send(client, "GET", Links(LabId), "student");
        using var missing = await Send(client, "GET", Links(MissingLabId), "student");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(await missing.Content.ReadAsStringAsync()));
        Assert.That(factory.Store.ReadCalls, Is.Zero);
        Assert.That(factory.Store.MutationCalls, Is.Zero);
        Assert.That(factory.Catalog.ReadCalls, Is.Zero);
    }

    [Test]
    public async Task TeacherCanReadOwnDraftWithoutPublishedContent()
    {
        using var factory = new Prg04Factory();
        factory.PublicContent.Content = null;
        factory.Store.Seed(LabId, CompetencyId);
        using var client = factory.CreateClient();
        using var response = await Send(client, "GET", Links(LabId), "teacher");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var links = await response.Content.ReadFromJsonAsync<LaboratoryCompetenciesResponse>();
        Assert.That(links!.Competencies.Select(x => x.Id), Is.EqualTo(new[] { CompetencyId }));
    }

    [TestCase("GET", true)]
    [TestCase("PUT", true)]
    [TestCase("DELETE", true)]
    [TestCase("GET", false)]
    [TestCase("PUT", false)]
    [TestCase("DELETE", false)]
    public async Task SwappingLaboratoryId_ReturnsSameResponseAsMissingAndNeverTouchesLinks(string method, bool otherCourse)
    {
        using var factory = new Prg04Factory();
        Guid targetLab = otherCourse ? ForeignLabId : OtherLabId;
        factory.Store.Seed(targetLab, CompetencyId);
        factory.Store.Seed(MissingLabId, CompetencyId); // Orphan data must not make the lab visible.
        using var client = factory.CreateClient();
        string existingPath = method == "GET" ? Links(targetLab) : Link(targetLab);
        string missingPath = method == "GET" ? Links(MissingLabId) : Link(MissingLabId);
        using var existing = await Send(client, method, existingPath, "teacher");
        using var missing = await Send(client, method, missingPath, "teacher");

        Assert.That(existing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(missing.StatusCode, Is.EqualTo(existing.StatusCode));
        Assert.That(await existing.Content.ReadAsStringAsync(), Is.EqualTo(await missing.Content.ReadAsStringAsync()));
        Assert.That(factory.Store.ReadCalls, Is.Zero);
        Assert.That(factory.Store.MutationCalls, Is.Zero);
        Assert.That(factory.Store.HasLink(targetLab, CompetencyId), Is.True);
        Assert.That(factory.Store.HasLink(MissingLabId, CompetencyId), Is.True);
    }

    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task LaboratoryOwnerWhoIsNotCourseTeacher_CannotEdit(string method)
    {
        using var factory = new Prg04Factory();
        factory.Membership.Set(Course, Teacher, CourseRole.Student);
        factory.Store.Seed(LabId, CompetencyId);
        using var client = factory.CreateClient();
        using var response = await Send(client, method, Link(LabId), "teacher");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Store.MutationCalls, Is.Zero);
        Assert.That(factory.Store.HasLink(LabId, CompetencyId), Is.True);
    }

    [TestCase("GET")]
    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task LaboratoryWithoutSavedCourse_IsDeniedEvenToItsCreator(string method)
    {
        using var factory = new Prg04Factory();
        factory.Store.Seed(LegacyLabId, CompetencyId);
        using var client = factory.CreateClient();
        using var response = await Send(client, method, method == "GET" ? Links(LegacyLabId) : Link(LegacyLabId), "teacher");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Store.ReadCalls, Is.Zero);
        Assert.That(factory.Store.MutationCalls, Is.Zero);
    }

    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task UnknownCompetency_IsRejectedBeforeMutation(string method)
    {
        using var factory = new Prg04Factory();
        using var client = factory.CreateClient();
        using var response = await Send(client, method, Link(LabId, Guid.NewGuid()), "teacher");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await response.Content.ReadFromJsonAsync<CompetencyErrorResponse>())!.Error,
            Is.EqualTo("competency_not_found"));
        Assert.That(factory.Store.MutationCalls, Is.Zero);
    }

    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task SpoofedCourseAndCallerFields_DoNotChangeAuthorization(string method)
    {
        using var factory = new Prg04Factory();
        factory.Store.Seed(ForeignLabId, CompetencyId);
        using var client = factory.CreateClient();
        string path = Link(ForeignLabId) + $"?courseId={Course:D}&createdBy={Teacher:D}&userId={ForeignTeacher:D}";
        using var request = Request(method, path, "teacher");
        request.Headers.Add("X-Test-User", $"{ForeignTeacher:D}:Teacher");
        request.Headers.Add("X-Stub-User", ForeignTeacher.ToString("D"));
        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Store.MutationCalls, Is.Zero);
        Assert.That(factory.Store.HasLink(ForeignLabId, CompetencyId), Is.True);
    }

    [Test]
    public async Task ChangingMembershipAfterSuccessfulEdit_IsCheckedOnTheNextRequest()
    {
        using var factory = new Prg04Factory();
        using var client = factory.CreateClient();
        using var first = await Send(client, "PUT", Link(LabId), "teacher");
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        factory.Membership.Set(Course, Teacher, CourseRole.None);
        int mutationsBefore = factory.Store.MutationCalls;
        using var denied = await Send(client, "DELETE", Link(LabId), "teacher");

        Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Store.MutationCalls, Is.EqualTo(mutationsBefore));
        Assert.That(factory.Store.HasLink(LabId, CompetencyId), Is.True);
    }

    [Test]
    public async Task AuthenticatedCaller_Gets503WhenCompetencyPersistenceIsNotConfigured()
    {
        using var factory = new Prg04Factory(includeCompetencyStores: false);
        using var client = factory.CreateClient();
        using var response = await Send(client, "GET", "/api/competencies", "teacher");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task EmptyCompetencyId_IsRejectedWithoutMutation(string method)
    {
        using var factory = new Prg04Factory();
        using var client = factory.CreateClient();
        using var response = await Send(client, method, Link(LabId, Guid.Empty), "teacher");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(factory.Store.MutationCalls, Is.Zero);
    }

    private static string Links(Guid laboratoryId) => $"/api/laboratories/{laboratoryId:D}/competencies";
    private static string Link(Guid laboratoryId) => Link(laboratoryId, CompetencyId);
    private static string Link(Guid laboratoryId, Guid competencyId) => $"{Links(laboratoryId)}/{competencyId:D}";
    private static HttpRequestMessage Request(string method, string path, string? token)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (token is not null) request.Headers.Add("Cookie", $"{SessionCookie.Name}={token}");
        return request;
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string method, string path, string? token)
    {
        using var request = Request(method, path, token);
        return await client.SendAsync(request);
    }

    private sealed class Prg04Factory : WebApplicationFactory<Program>
    {
        private readonly string _environment;
        private readonly bool _includeCompetencyStores;
        private readonly bool _hideManagePolicy;
        public TestLaboratories Laboratories { get; } = new();
        public InMemoryCourseMembership Membership { get; } = new();
        public RecordingCatalog Catalog { get; } = new();
        public RecordingStore Store { get; } = new();
        public TestPublicContentReader PublicContent { get; } = new();

        public Prg04Factory(string environment = "Production", bool includeCompetencyStores = true, bool hideManagePolicy = false)
        {
            _environment = environment;
            _includeCompetencyStores = includeCompetencyStores;
            _hideManagePolicy = hideManagePolicy;
            var now = DateTimeOffset.UtcNow;
            Laboratories.AddAsync(Laboratory.Rehydrate(LabId, Course, "Own lab", LabStatus.Draft, Teacher, now)).GetAwaiter().GetResult();
            Laboratories.AddAsync(Laboratory.Rehydrate(OtherLabId, Course, "Other author's lab", LabStatus.Draft, OtherTeacher, now)).GetAwaiter().GetResult();
            Laboratories.AddAsync(Laboratory.Rehydrate(ForeignLabId, ForeignCourse, "Foreign course lab", LabStatus.Draft, ForeignTeacher, now)).GetAwaiter().GetResult();
            Laboratories.AddAsync(Laboratory.Rehydrate(LegacyLabId, "No course", LabStatus.Draft, Teacher, now)).GetAwaiter().GetResult();
            Membership.Set(Course, Teacher, CourseRole.Teacher);
            Membership.Set(Course, Student, CourseRole.Student);
            Membership.Set(Course, OtherTeacher, CourseRole.Teacher);
            Membership.Set(ForeignCourse, ForeignTeacher, CourseRole.Teacher);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            // Avoid developer secrets, migrations or a database on the real machine.
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = "",
                ["ConnectionStrings:Competencies"] = "",
                ["ConnectionStrings:Laboratories"] = "",
                ["ConnectionStrings:Authoring"] = "",
            }));
            builder.ConfigureLogging(log => log.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISessionManager>();
                services.RemoveAll<IAccountStatusReader>();
                services.RemoveAll<ILaboratoryRepository>();
                services.RemoveAll<ICourseMembership>();
                services.RemoveAll<ICompetencyCatalog>();
                services.RemoveAll<ILaboratoryCompetencyStore>();
                services.RemoveAll<ILabPublicContentReader>();
                services.AddSingleton<ISessionManager, TestSessions>();
                services.AddSingleton<IAccountStatusReader, TestAccounts>();
                services.AddSingleton<ILaboratoryRepository>(Laboratories);
                services.AddSingleton<ICourseMembership>(Membership);
                services.AddSingleton<ILabPublicContentReader>(PublicContent);
                if (_hideManagePolicy)
                {
                    // Keep startup's complete catalog, but make the real decision engine
                    // observe an absent policy for this request-level fail-closed test.
                    services.RemoveAll<IAccessPolicyRegistry>();
                    services.AddSingleton<IAccessPolicyRegistry>(provider => new MissingManagePolicyRegistry(
                        provider.GetRequiredService<InMemoryAccessPolicyRegistry>()));
                }
                if (_includeCompetencyStores)
                {
                    services.AddSingleton<ICompetencyCatalog>(Catalog);
                    services.AddSingleton<ILaboratoryCompetencyStore>(Store);
                }
            });
        }
    }

    private sealed class TestSessions : ISessionManager
    {
        public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default)
        {
            Guid? actor = token switch
            {
                "teacher" => Teacher, "student" => Student, "other-teacher" => OtherTeacher,
                "foreign-teacher" => ForeignTeacher, "blocked" => Blocked, "pending" => Teacher,
                "empty-user" => Guid.Empty, "empty-handle" => Teacher,
                "unknown-account" => UnknownAccount, _ => null,
            };
            return Task.FromResult(actor is { } id
                ? new ValidateSession(token == "empty-handle" ? Guid.Empty : Guid.NewGuid(), id,
                    token == "pending" ? AuthenticationLevel.MfaPending : AuthenticationLevel.Active)
                : null);
        }
        public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    // Keeps the trusted repository's exact return values, including a legacy row
    // without CourseId. This models the current PostgreSQL adapter's boundary.
    private sealed class TestLaboratories : ILaboratoryRepository
    {
        private readonly Dictionary<Guid, Laboratory> _laboratories = [];
        public Task AddAsync(Laboratory laboratory, CancellationToken cancellationToken = default)
        {
            _laboratories.Add(laboratory.Id, laboratory);
            return Task.CompletedTask;
        }
        public Task<Laboratory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_laboratories.GetValueOrDefault(id));
        public Task<IReadOnlyList<Laboratory>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Laboratory>>(_laboratories.Values.ToArray());
        public Task UpdateAsync(Laboratory laboratory, CancellationToken cancellationToken = default)
        {
            _laboratories[laboratory.Id] = laboratory;
            return Task.CompletedTask;
        }
        public Task<LabRevision> AddRevisionAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LabRevision>> GetRevisionsAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestAccounts : IAccountStatusReader
    {
        public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountSnapshot?>(accountId == UnknownAccount ? null : new AccountSnapshot(accountId,
                accountId == Blocked ? AccountStatus.Blocked : AccountStatus.Active));
    }

    private sealed class TestPublicContentReader : ILabPublicContentReader
    {
        public LabPublicContent? Content { get; set; } = LabPublicContent.Create(
            LabId, Course, Teacher, LabVisibilityState.Published, "Published lab", "Goal", []);

        public ValueTask<LabPublicContent?> GetAsync(Guid labId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Content);
    }

    private sealed class MissingManagePolicyRegistry(IAccessPolicyRegistry inner) : IAccessPolicyRegistry
    {
        public bool TryGet(string action, out IAccessPolicy? policy)
        {
            if (action == CompetencyLinkActions.Manage)
            {
                policy = null;
                return false;
            }
            return inner.TryGet(action, out policy);
        }
    }

    private sealed class RecordingCatalog : ICompetencyCatalog
    {
        public int ReadCalls { get; private set; }
        public Task<IReadOnlyList<Competency>> ListAsync(CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(BuiltInCompetencies.All);
        }
    }

    private sealed class RecordingStore : ILaboratoryCompetencyStore
    {
        private readonly HashSet<(Guid LaboratoryId, Guid CompetencyId)> _links = [];
        public int ReadCalls { get; private set; }
        public int MutationCalls { get; private set; }
        public void Seed(Guid laboratoryId, Guid competencyId) => _links.Add((laboratoryId, competencyId));
        public bool HasLink(Guid laboratoryId, Guid competencyId) => _links.Contains((laboratoryId, competencyId));
        public Task<bool> AddAsync(Guid laboratoryId, Guid competencyId, CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            return Task.FromResult(_links.Add((laboratoryId, competencyId)));
        }
        public Task<bool> RemoveAsync(Guid laboratoryId, Guid competencyId, CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            return Task.FromResult(_links.Remove((laboratoryId, competencyId)));
        }
        public Task<IReadOnlyList<Competency>> GetForLaboratoryAsync(Guid laboratoryId, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult<IReadOnlyList<Competency>>(BuiltInCompetencies.All
                .Where(c => _links.Contains((laboratoryId, c.Id))).ToArray());
        }
    }
}
