using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratorySearchHttpTests
{
    private const string LabsRoute = "/api/laboratories/";
    private const string SearchRoute = "/api/laboratories/search";
    private const string TestUserHeader = "X-Test-User";
    private const string Teacher = "marko:Teacher";
    private const string OtherTeacher = "jelena:Teacher";
    private const string Student = "ana:Student";

    [TestCase(null)]
    [TestCase("invalid")]
    [TestCase("ana:Unknown")]
    public async Task Search_WithoutValidIdentity_Returns401(string? testUser)
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        await CreateLabAsync(client, Teacher, "RBAC i kontrola pristupa");

        using var response = await client.SendAsync(Get($"{SearchRoute}?q=rbac", testUser));
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(body, Does.Not.Contain("RBAC i kontrola pristupa"));
    }

    [Test]
    public async Task Search_WithMultipleIdentityHeaders_Returns401()
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        using var request = Get($"{SearchRoute}?q=rbac", null);
        request.Headers.Add(TestUserHeader, new[] { Student, Teacher });

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("Production", "/api/laboratories/search?q=rbac")]
    [TestCase("Staging", "/api/laboratories/search?q=rbac")]
    [TestCase("Staging", "/api/labs/search?q=rbac")]
    public async Task OutsideDevelopment_TestTeacherHeaderDoesNotGrantSearch(string environment, string path)
    {
        using var factory = new SearchHostFactory(environment);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Get(path, Teacher));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("kontrola")]
    [TestCase("KONTROLA")]
    [TestCase("RBAC%20i%20kontrola%20pristupa")]
    [TestCase("%20%20pristupa%20%20")]
    public async Task Search_WhenTeacherSearchesPartOfTitle_ReturnsOwnMatchingLab(string encodedQuery)
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse rbac = await CreateLabAsync(client, Teacher, "RBAC i kontrola pristupa");
        await CreateLabAsync(client, Teacher, "Analiza logova");

        using var response = await client.SendAsync(Get($"{SearchRoute}?q={encodedQuery}", Teacher));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        var result = await response.Content.ReadFromJsonAsync<LaboratorySearchResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Items, Is.EqualTo(new[] { AsSearchItem(rbac) }));
            Assert.That(result.Truncated, Is.False);
        });
    }

    [Test]
    public async Task Search_WithoutDiacritics_FindsTitleWithDiacritics()
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse lab = await CreateLabAsync(client, Teacher, "Šifrovanje i ključevi");

        LaboratorySearchResponse result = await SearchAsync(client, Teacher, "kljucevi");

        Assert.That(result.Items, Is.EqualTo(new[] { AsSearchItem(lab) }));
    }

    [Test]
    public async Task Search_WhenNothingMatches_Returns200WithEmptyItems()
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        await CreateLabAsync(client, Teacher, "Analiza logova");

        LaboratorySearchResponse result = await SearchAsync(client, Teacher, "firewall");

        Assert.That(result.Items, Is.Empty);
        Assert.That(result.Truncated, Is.False);
    }

    [Test]
    public async Task Search_ThroughAliasRoute_UsesSameChecks()
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse lab = await CreateLabAsync(client, Teacher, "Preko aliasa");

        using var teacher = await client.SendAsync(Get("/api/labs/search?q=alias", Teacher));
        using var anonymous = await client.SendAsync(Get("/api/labs/search?q=alias", null));

        Assert.That(teacher.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await teacher.Content.ReadFromJsonAsync<LaboratorySearchResponse>())!.Items, Is.EqualTo(new[] { AsSearchItem(lab) }));
        Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Search_DoesNotReturnLabsOfAnotherTeacher()
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse foreign = await CreateLabAsync(client, Teacher, "Tajni RBAC nacrt");

        using var response = await client.SendAsync(Get($"{SearchRoute}?q=rbac", OtherTeacher));
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Not.Contain("Tajni RBAC nacrt"));
            Assert.That(body, Does.Not.Contain(foreign.Id.ToString()));
            Assert.That(body, Does.Contain("\"items\":[]"));
        });
    }

    [Test]
    public async Task Search_WhenStudentSearches_DoesNotSeeDrafts()
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse draft = await CreateLabAsync(client, Teacher, "RBAC nacrt nije za studente");

        using var response = await client.SendAsync(Get($"{SearchRoute}?q=rbac", Student));
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Not.Contain("RBAC nacrt nije za studente"));
            Assert.That(body, Does.Not.Contain(draft.Id.ToString()));
            Assert.That(body, Does.Contain("\"items\":[]"));
        });
    }

    [TestCase(Teacher)]
    [TestCase(OtherTeacher)]
    [TestCase(Student)]
    public async Task Search_EveryResultCanBeOpenedBySameCaller(string caller)
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        await CreateLabAsync(client, Teacher, "Lab kontrola pristupa A");
        await CreateLabAsync(client, Teacher, "Lab kontrola pristupa B");
        await CreateLabAsync(client, OtherTeacher, "Lab kontrola pristupa C");

        LaboratorySearchResponse result = await SearchAsync(client, caller, "kontrola");

        foreach (LaboratorySearchItem item in result.Items)
        {
            using var read = await client.SendAsync(Get($"{LabsRoute}{item.Id}", caller));
            Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Rezultat {item.Id} nije dostupan pozivaocu.");
            Assert.That(AsSearchItem((await read.Content.ReadFromJsonAsync<LaboratoryResponse>())!), Is.EqualTo(item));
        }

        int expected = caller switch
        {
            Teacher => 2,
            OtherTeacher => 1,
            _ => 0,
        };
        Assert.That(result.Items, Has.Count.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("?q=")]
    [TestCase("?q=%20%20%20")]
    [TestCase("?q=rbac%0Aadmin")]
    [TestCase("?q=%00")]
    [TestCase("?q=a&q=b")]
    [TestCase("?query=rbac")]
    public async Task Search_WithMissingBlankOrInvalidQuery_Returns400WithoutInternalDetails(string queryString)
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Get($"{SearchRoute}{queryString}", Teacher));
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain(LabErrorCodes.InvalidQuery));
            Assert.That(body, Does.Not.Contain("admin"));
            Assert.That(body, Does.Not.Contain("Exception"));
            Assert.That(body, Does.Not.Contain(" at "));
        });
    }

    [Test]
    public async Task Search_WithQueryLongerThanLongestTitle_Returns400()
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        string query = new('a', Laboratory.MaxTitleLength + 1);

        using var response = await client.SendAsync(Get($"{SearchRoute}?q={query}", Teacher));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain(query));
    }

    [Test]
    public async Task Search_WithInjectionLikeQuery_IsPlainTextAndDoesNotReturnEverything()
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        await CreateLabAsync(client, Teacher, "RBAC i kontrola pristupa");
        await CreateLabAsync(client, Teacher, "Analiza logova");

        LaboratorySearchResponse result = await SearchAsync(client, Teacher, "' OR '1'='1");

        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task Search_HtmlInTitle_IsReturnedAsJsonData()
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse lab = await CreateLabAsync(client, Teacher, "<script>alert(1)</script> XSS vezba");

        using var response = await client.SendAsync(Get($"{SearchRoute}?q={Uri.EscapeDataString("<script>")}", Teacher));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        var result = await response.Content.ReadFromJsonAsync<LaboratorySearchResponse>();
        Assert.That(result!.Items, Is.EqualTo(new[] { AsSearchItem(lab) }));
    }

    [Test]
    public async Task Search_WhenStorageFails_Returns500WithoutInternalDetails()
    {
        using var factory = new SearchHostFactory("Development", services =>
            services.AddSingleton<ILaboratoryRepository, FailingRepository>());
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Get($"{SearchRoute}?q=rbac", Teacher));
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Not.Contain(FailingRepository.SecretDetail));
            Assert.That(body, Does.Not.Contain("Exception"));
            Assert.That(body, Does.Contain("traceId"));
        });
    }

    [Test]
    public async Task Search_ResultsDoNotExposeAuthorId()
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse lab = await CreateLabAsync(client, Teacher, "RBAC i kontrola pristupa");

        using var response = await client.SendAsync(Get($"{SearchRoute}?q=rbac", Teacher));
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain(lab.Id.ToString()));
            Assert.That(body, Does.Not.Contain("createdBy"));
            Assert.That(body, Does.Not.Contain(lab.CreatedBy.ToString()));
        });
    }

    [TestCase("?q=rbac", HttpStatusCode.OK)]
    [TestCase("?q=", HttpStatusCode.BadRequest)]
    public async Task Search_ResponseIsNeverCached(string queryString, HttpStatusCode expectedStatus)
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        await CreateLabAsync(client, Teacher, "RBAC i kontrola pristupa");

        using var response = await client.SendAsync(Get($"{SearchRoute}{queryString}", Teacher));

        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [TestCase("petar:Assistant")]
    [TestCase("admin:Administrator")]
    public async Task Search_WhenAssistantOrAdministratorSearches_DoesNotSeeDrafts(string caller)
    {
        using var factory = new SearchHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse draft = await CreateLabAsync(client, Teacher, "RBAC nacrt nastavnika");

        using var response = await client.SendAsync(Get($"{SearchRoute}?q=rbac", caller));
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("\"items\":[]"));
            Assert.That(body, Does.Not.Contain("RBAC nacrt nastavnika"));
            Assert.That(body, Does.Not.Contain(draft.Id.ToString()));
        });
    }

    [Test]
    public async Task Search_WhenAccessCheckFails_Returns500WithoutResults()
    {
        using var factory = new SearchHostFactory("Development", services =>
            services.AddSingleton<IAccessDecision, ReadCheckFailsAccessDecision>());
        using var client = factory.CreateClient();
        LaboratoryResponse lab = await CreateLabAsync(client, Teacher, "RBAC i kontrola pristupa");

        using var response = await client.SendAsync(Get($"{SearchRoute}?q=rbac", Teacher));
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Not.Contain("RBAC i kontrola pristupa"));
            Assert.That(body, Does.Not.Contain(lab.Id.ToString()));
            Assert.That(body, Does.Not.Contain(ReadCheckFailsAccessDecision.SecretDetail));
            Assert.That(body, Does.Contain("traceId"));
        });
    }

    private static LaboratorySearchItem AsSearchItem(LaboratoryResponse lab) =>
        new(lab.Id, lab.Title, lab.Status, lab.CreatedAt);

    private static async Task<LaboratoryResponse> CreateLabAsync(HttpClient client, string user, string title)
    {
        using var request = Get(LabsRoute, user);
        request.Method = HttpMethod.Post;
        request.Content = JsonContent.Create(new { title });
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await response.Content.ReadFromJsonAsync<LaboratoryResponse>();
        Assert.That(created, Is.Not.Null);
        return created!;
    }

    private static async Task<LaboratorySearchResponse> SearchAsync(HttpClient client, string user, string query)
    {
        using var response = await client.SendAsync(Get($"{SearchRoute}?q={Uri.EscapeDataString(query)}", user));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<LaboratorySearchResponse>();
        Assert.That(result, Is.Not.Null);
        return result!;
    }

    private static HttpRequestMessage Get(string path, string? testUser)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (testUser is not null)
        {
            request.Headers.Add(TestUserHeader, testUser);
        }

        return request;
    }

    private sealed class SearchHostFactory(
        string environment, Action<IServiceCollection>? testServices = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            if (testServices is not null)
            {
                builder.ConfigureTestServices(testServices);
            }
        }
    }

    private sealed class FailingRepository : ILaboratoryRepository
    {
        public const string SecretDetail = "connection-string=Host=db;Password=do-not-leak";

        private static Exception Fail() => new InvalidOperationException(SecretDetail);

        public Task AddAsync(Laboratory laboratory, CancellationToken cancellationToken = default) => throw Fail();
        public Task<Laboratory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw Fail();
        public Task<IReadOnlyList<Laboratory>> ListAsync(CancellationToken cancellationToken = default) => throw Fail();
        public Task UpdateAsync(Laboratory laboratory, CancellationToken cancellationToken = default) => throw Fail();
        public Task<LabRevision> AddRevisionAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw Fail();
        public Task<IReadOnlyList<LabRevision>> GetRevisionsAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw Fail();
    }

    private sealed class ReadCheckFailsAccessDecision : IAccessDecision
    {
        public const string SecretDetail = "policy-store=Host=acl;Password=do-not-leak";

        private readonly FakeAccessDecision _inner = new();

        public AccessResult Decide(IIdentityContext subject, string action, string? resourceId = null) =>
            action == LabActions.Read
                ? throw new InvalidOperationException(SecretDetail)
                : _inner.Decide(subject, action, resourceId);
    }
}
