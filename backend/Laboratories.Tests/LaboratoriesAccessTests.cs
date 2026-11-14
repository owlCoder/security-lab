using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Infrastructure;

namespace Laboratories.Tests;

// #185: rute laboratorija prolaze kroz centralni ACC-05 middleware (RequireAccess),
// a ishodi (401/403) ostaju isti kao sa RequireLabAccess.
[TestFixture]
public sealed class LaboratoriesAccessTests
{
    private const string TestUserHeader = "X-Test-User";
    private const string Teacher = "marko:Teacher";
    private const string Student = "ana:Student";

    // ---------- Metapodaci rute ----------

    [Test]
    public void EveryLaboratoryRoute_HasExactlyOneRequireAccessWithItsAction()
    {
        using var factory = new HostFactory("Development");
        List<RouteEndpoint> endpoints = LaboratoryEndpoints(factory);

        var expected = new Dictionary<string, string>
        {
            ["GET /"] = LabActions.List,
            ["POST /"] = LabActions.Create,
            ["GET /{id:guid}"] = LabActions.Read,
            ["PATCH /{id:guid}/title"] = LabActions.Rename,
        };

        // Cetiri rute na kanonskoj putanji i cetiri na aliasu.
        Assert.That(endpoints, Has.Count.EqualTo(8));
        Assert.Multiple(() =>
        {
            foreach (RouteEndpoint endpoint in endpoints)
            {
                string key = RouteKey(endpoint);
                IReadOnlyList<IAccessRequirementMetadata> requirements =
                    endpoint.Metadata.GetOrderedMetadata<IAccessRequirementMetadata>();

                Assert.That(requirements, Has.Count.EqualTo(1), key);
                Assert.That(requirements[0].Action, Is.EqualTo(expected[key]), key);
                Assert.That(requirements[0].ResourceType, Is.Null, key);
                Assert.That(endpoint.Metadata.Any(m => m.GetType().Name == "DelegatedAuthorizationAttribute"),
                    Is.False, $"{key}: legacy delegation must be gone");
            }
        });
    }

    // ---------- Isti ishodi kroz centralni middleware ----------

    [TestCase("/api/laboratories")]
    [TestCase("/api/labs")]
    public async Task Anonymous_GetsUnauthorizedOnEveryRoute(string basePath)
    {
        using var factory = new HostFactory("Development");
        using var client = factory.CreateClient();

        foreach (HttpRequestMessage request in AllRoutes(basePath, testUser: null))
        {
            using var response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), $"{request.Method} {request.RequestUri}");
        }
    }

    [TestCase("/api/laboratories")]
    [TestCase("/api/labs")]
    public async Task InvalidTestUserHeader_GetsUnauthorized(string basePath)
    {
        using var factory = new HostFactory("Development");
        using var client = factory.CreateClient();

        foreach (HttpRequestMessage request in AllRoutes(basePath, testUser: "ana:Unknown"))
        {
            using var response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), $"{request.Method} {request.RequestUri}");
        }
    }

    [TestCase("/api/laboratories")]
    [TestCase("/api/labs")]
    public async Task Student_IsDeniedByCentralPolicyOnCreateReadRename_AndNothingIsStored(string basePath)
    {
        using var factory = new HostFactory("Development");
        using var client = factory.CreateClient();
        Guid labId = Guid.NewGuid();

        HttpRequestMessage[] requests =
        [
            JsonRequest(HttpMethod.Post, $"{basePath}/", Student, new { title = "Studentski pokusaj", courseId = Guid.NewGuid() }),
            Request(HttpMethod.Get, $"{basePath}/{labId}", Student),
            JsonRequest(HttpMethod.Patch, $"{basePath}/{labId}/title", Student, new { title = "Hakovano" }),
        ];

        foreach (HttpRequestMessage request in requests)
        {
            using var response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"{request.Method} {request.RequestUri}");
            // Odgovor pise ACC-05 middleware (correlationId), dakle odbijanje je pre filtera i servisa.
            Assert.That(body, Does.Contain("correlationId"), $"{request.Method} {request.RequestUri}");
        }

        var repository = factory.Services.GetRequiredService<ILaboratoryRepository>();
        Assert.That(await repository.ListAsync(), Is.Empty);
    }

    [Test]
    public async Task Student_CanStillList_AsBefore()
    {
        using var factory = new HostFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Request(HttpMethod.Get, "/api/laboratories/", Student));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase("Production")]
    [TestCase("Staging")]
    public async Task OutsideDevelopment_TeacherHeaderIsIgnoredByCentralMiddleware(string environment)
    {
        using var factory = new HostFactory(environment);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            JsonRequest(HttpMethod.Post, "/api/laboratories/", Teacher, new { title = "Ne sme proci", courseId = Guid.NewGuid() }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // ---------- LaboratoryAccessPolicy ----------

    [TestCase(Teacher, LabActions.Create, true)]
    [TestCase(Teacher, LabActions.Read, true)]
    [TestCase(Student, LabActions.List, true)]
    [TestCase(Student, LabActions.Create, false)]
    [TestCase(Student, LabActions.Read, false)]
    [TestCase(Student, LabActions.Rename, false)]
    public async Task Policy_GivesSameAnswerAsFakeAccessDecision(string testUser, string action, bool expected)
    {
        var policy = new LaboratoryAccessPolicy(Accessor(testUser), new FakeAccessDecision());

        bool allowed = await policy.EvaluateAsync(new AccessRequest(ActiveSubject, action, null, "test"));

        Assert.That(allowed, Is.EqualTo(expected));
    }

    [Test]
    public async Task Policy_WithoutSubject_Denies()
    {
        var policy = new LaboratoryAccessPolicy(Accessor(Teacher), new FakeAccessDecision());

        Assert.That(await policy.EvaluateAsync(new AccessRequest(null, LabActions.Create, null, "test")), Is.False);
    }

    [Test]
    public async Task Policy_WithUnexpectedResource_Denies()
    {
        var policy = new LaboratoryAccessPolicy(Accessor(Teacher), new FakeAccessDecision());
        var request = new AccessRequest(ActiveSubject, LabActions.Read, new ResourceRef("laboratory", "x"), "test");

        Assert.That(await policy.EvaluateAsync(request), Is.False);
    }

    [Test]
    public async Task Policy_OutsideDevelopment_DeniesEvenTeacher()
    {
        var policy = new LaboratoryAccessPolicy(Accessor(Teacher), new DenyAllAccessDecision());

        Assert.That(await policy.EvaluateAsync(new AccessRequest(ActiveSubject, LabActions.Create, null, "test")), Is.False);
    }

    // ---------- LaboratorySubjectProvider ----------

    [Test]
    public async Task SubjectProvider_OnLaboratoryRoute_UsesModuleIdentity()
    {
        var inner = new CountingProvider();
        var provider = new LaboratorySubjectProvider(inner);
        HttpContext http = Context(Teacher, LabActions.Read);

        SubjectRef? subject = await provider.GetSubjectAsync(http);

        Assert.That(subject, Is.EqualTo(new SubjectRef(
            CallerId.From(http.RequestServices.GetRequiredService<IIdentityContext>())!.Value.ToString("D"),
            AccountState.Active)));
        Assert.That(inner.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task SubjectProvider_OnLaboratoryRouteWithoutIdentity_ReturnsNull()
    {
        var provider = new LaboratorySubjectProvider(new CountingProvider());

        Assert.That(await provider.GetSubjectAsync(Context(testUser: null, LabActions.Create)), Is.Null);
    }

    [TestCase("lab.preview")]
    [TestCase(null)]
    public async Task SubjectProvider_OnOtherRoute_UsesOriginalProvider(string? action)
    {
        var inner = new CountingProvider();
        var provider = new LaboratorySubjectProvider(inner);

        SubjectRef? subject = await provider.GetSubjectAsync(Context(Teacher, action));

        Assert.That(subject, Is.SameAs(CountingProvider.Subject));
        Assert.That(inner.Calls, Is.EqualTo(1));
    }

    // ---------- Pomocne metode ----------

    private static readonly SubjectRef ActiveSubject = new("subject-1", AccountState.Active);

    private static List<RouteEndpoint> LaboratoryEndpoints(WebApplicationFactory<Program> factory) =>
        factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ITagsMetadata>()?.Tags.Contains("Laboratories") == true)
            .ToList();

    private static string RouteKey(RouteEndpoint endpoint)
    {
        string method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single();
        string pattern = endpoint.RoutePattern.RawText!;
        string relative = pattern
            .Replace(LaboratoriesEndpoints.BasePath, string.Empty)
            .Replace(LaboratoriesEndpoints.AliasPath, string.Empty);
        return $"{method} {(relative.Length == 0 ? "/" : relative)}";
    }

    private static IEnumerable<HttpRequestMessage> AllRoutes(string basePath, string? testUser)
    {
        Guid labId = Guid.NewGuid();
        yield return Request(HttpMethod.Get, $"{basePath}/", testUser);
        yield return JsonRequest(HttpMethod.Post, $"{basePath}/", testUser, new { title = "Lab", courseId = Guid.NewGuid() });
        yield return Request(HttpMethod.Get, $"{basePath}/{labId}", testUser);
        yield return JsonRequest(HttpMethod.Patch, $"{basePath}/{labId}/title", testUser, new { title = "Lab" });
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? testUser)
    {
        var request = new HttpRequestMessage(method, path);
        if (testUser is not null)
        {
            request.Headers.Add(TestUserHeader, testUser);
        }

        return request;
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, string? testUser, object body)
    {
        HttpRequestMessage request = Request(method, path, testUser);
        request.Content = JsonContent.Create(body);
        return request;
    }

    // HTTP kontekst sa X-Test-User zaglavljem i (opciono) RequireAccess metapodatkom rute.
    private static HttpContext Context(string? testUser, string? action)
    {
        var http = new DefaultHttpContext();
        if (testUser is not null)
        {
            http.Request.Headers[TestUserHeader] = testUser;
        }

        var accessor = new HttpContextAccessor { HttpContext = http };
        http.RequestServices = new ServiceCollection()
            .AddSingleton<IHttpContextAccessor>(accessor)
            .AddScoped<IIdentityContext, FakeIdentityContext>()
            .BuildServiceProvider();

        EndpointMetadataCollection metadata = action is null
            ? new EndpointMetadataCollection()
            : new EndpointMetadataCollection(new RequireAccessAttribute(action));
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));
        return http;
    }

    private static IHttpContextAccessor Accessor(string testUser) =>
        Context(testUser, action: null).RequestServices.GetRequiredService<IHttpContextAccessor>();

    private sealed class CountingProvider : IAccessSubjectProvider
    {
        public static readonly SubjectRef Subject = new("from-inner", AccountState.Active);

        public int Calls { get; private set; }

        public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult<SubjectRef?>(Subject);
        }
    }

    private sealed class HostFactory(string environment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
