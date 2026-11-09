using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Membership;

namespace Laboratories.Tests;

// HTTP testovi LAB-01 i LAB-03 API-ja nad stvarnim hostom u memoriji (WebApplicationFactory).
// Svaki test pravi svoj host, pa ima svoje prazno skladiste.
[TestFixture]
public sealed class LaboratoriesHttpTests
{
    private const string Route = "/api/laboratories/";
    private const string TestUserHeader = "X-Test-User";
    private const string Teacher = "marko:Teacher";
    private const string OtherTeacher = "jelena:Teacher";
    private const string Student = "ana:Student";

    // LAB-03: marko je nastavnik, a ana studentkinja na CourseId (postavlja LaboratoriesHostFactory).
    // Na OtherCourseId niko od njih nije clan.
    private static readonly Guid CourseId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherCourseId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    // ---------- Provera pristupa pre endpoint-a (kostur) ----------

    [TestCase("GET", null, HttpStatusCode.Unauthorized)]
    [TestCase("POST", null, HttpStatusCode.Unauthorized)]
    [TestCase("GET", Student, HttpStatusCode.OK)]
    [TestCase("POST", Student, HttpStatusCode.Forbidden)]
    [TestCase("GET", Teacher, HttpStatusCode.OK)]
    [TestCase("GET", "ana:Unknown", HttpStatusCode.Unauthorized)]
    [TestCase("GET", "invalid", HttpStatusCode.Unauthorized)]
    public async Task Development_EnforcesAccessBeforeEndpoint(
        string method, string? testUser, HttpStatusCode expectedStatus)
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        using var request = CreateRequest(method, Route, testUser);

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        if (expectedStatus == HttpStatusCode.OK)
        {
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("[]"));
        }
    }

    [Test]
    public async Task Development_WithMultipleIdentityHeaders_Denies()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        using var request = CreateRequest("POST", Route, null);
        request.Headers.Add(TestUserHeader, new[] { Student, Teacher });

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Development_IdentityDoesNotLeakBetweenRequests()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        using var teacherResponse = await client.SendAsync(CreateJsonRequest("POST", Route, Teacher, new { title = "Lab nastavnika", courseId = CourseId }));
        Assert.That(teacherResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var studentResponse = await client.SendAsync(CreateJsonRequest("POST", Route, Student, new { title = "Lab studenta", courseId = CourseId }));
        Assert.That(studentResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        using var anonymousResponse = await client.SendAsync(CreateJsonRequest("POST", Route, null, new { title = "Anonimni lab", courseId = CourseId }));
        Assert.That(anonymousResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        Assert.That(await StoredCountAsync(factory), Is.EqualTo(1));
    }

    [TestCase("Production", "GET", "/api/laboratories/")]
    [TestCase("Production", "POST", "/api/laboratories/")]
    [TestCase("Production", "GET", "/api/laboratories/11111111-1111-1111-1111-111111111111")]
    [TestCase("Production", "PATCH", "/api/laboratories/11111111-1111-1111-1111-111111111111/title")]
    [TestCase("Staging", "GET", "/api/laboratories/")]
    [TestCase("Staging", "POST", "/api/laboratories/")]
    [TestCase("Staging", "POST", "/api/labs/")]
    [TestCase("Staging", "PATCH", "/api/labs/11111111-1111-1111-1111-111111111111/title")]
    public async Task OutsideDevelopment_TestTeacherHeaderDoesNotGrantAccess(
        string environment, string method, string path)
    {
        using var factory = new LaboratoriesHostFactory(environment);
        using var client = factory.CreateClient();
        using var request = CreateJsonRequest(method, path, Teacher, new { title = "Ne sme proci" });

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // ---------- POST: kreiranje ----------

    [Test]
    public async Task PostLabs_WhenTeacherCreates_Returns201WithServerIdAndLocation()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateJsonRequest("POST", Route, Teacher, new { title = "  RBAC i kontrola pristupa  ", courseId = CourseId }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await response.Content.ReadFromJsonAsync<LaboratoryResponse>();
        Assert.That(created, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(created!.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(created.Title, Is.EqualTo("RBAC i kontrola pristupa"));
            Assert.That(created.Status, Is.EqualTo("Draft"));
            Assert.That(created.CreatedBy, Is.Not.EqualTo(Guid.Empty));
            Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo($"/api/laboratories/{created.Id}"));
        });
    }

    [Test]
    public async Task PostLabs_WhenStudentCreates_Returns403AndStoresNothing()
    {
        // LAB-03: ana je upisana na kurs, ali student ne pravi laboratorije.
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateJsonRequest("POST", Route, Student, new { title = "Studentski pokusaj", courseId = CourseId }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await StoredCountAsync(factory), Is.EqualTo(0));
    }

    // ---------- POST: vezivanje za kurs (LAB-03) ----------

    [Test]
    public async Task PostLabs_WhenTeacherCreatesOnOwnCourse_Returns201WithCourse()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Lab na kursu");

        Assert.That(created.CourseId, Is.EqualTo(CourseId));
        var repository = factory.Services.GetRequiredService<ILaboratoryRepository>();
        Assert.That((await repository.GetByIdAsync(created.Id))!.CourseId, Is.EqualTo(CourseId));
    }

    [TestCase(null)]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public async Task PostLabs_WithoutCourse_Returns400AndStoresNothing(string? courseId)
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateJsonRequest("POST", Route, Teacher, new { title = "Bez kursa", courseId }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(LabErrorCodes.InvalidCourse));
        Assert.That(await StoredCountAsync(factory), Is.EqualTo(0));
    }

    [Test]
    public async Task PostLabs_WhenTeacherIsNotMemberOfCourse_Returns403AndStoresNothing()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateJsonRequest("POST", Route, OtherTeacher, new { title = "Tudji kurs", courseId = CourseId }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await StoredCountAsync(factory), Is.EqualTo(0));
    }

    [Test]
    public async Task PostLabs_WhenTeacherSwapsCourseId_Returns403AndStoresNothing()
    {
        // marko je nastavnik na CourseId; zamenom ID-a u telu ne moze da pravi na drugom kursu.
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateJsonRequest("POST", Route, Teacher, new { title = "Zamenjen kurs", courseId = OtherCourseId }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await StoredCountAsync(factory), Is.EqualTo(0));
    }

    [Test]
    public async Task PostLabs_DenialIsSameForExistingAndUnknownCourse()
    {
        // Odgovor ne sme da otkrije da li kurs postoji.
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        Guid unknownCourse = Guid.NewGuid();

        using var existing = await client.SendAsync(CreateJsonRequest("POST", Route, OtherTeacher, new { title = "Lab", courseId = CourseId }));
        using var unknown = await client.SendAsync(CreateJsonRequest("POST", Route, OtherTeacher, new { title = "Lab", courseId = unknownCourse }));
        string existingBody = await existing.Content.ReadAsStringAsync();
        string unknownBody = await unknown.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(existing.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(unknown.StatusCode, Is.EqualTo(existing.StatusCode));
            Assert.That(unknownBody, Is.EqualTo(existingBody));
            Assert.That(existingBody, Does.Not.Contain(CourseId.ToString()));
            Assert.That(unknownBody, Does.Not.Contain(unknownCourse.ToString()));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task PostLabs_WhenTitleIsMissingOrBlank_Returns400AndStoresNothing(string? invalidTitle)
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateJsonRequest("POST", Route, Teacher, new { title = invalidTitle, courseId = CourseId }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(LabErrorCodes.InvalidTitle));
        Assert.That(await StoredCountAsync(factory), Is.EqualTo(0));
    }

    [Test]
    public async Task PostLabs_WhenTitleIsTooLong_Returns400AndStoresNothing()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateJsonRequest("POST", Route, Teacher, new { title = new string('a', 201), courseId = CourseId }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await StoredCountAsync(factory), Is.EqualTo(0));
    }

    [TestCase("{ not json", "application/json")]
    [TestCase("", "application/json")]
    [TestCase("[]", "application/json")]
    [TestCase("{\"title\":\"Lab\"}", "text/plain")]
    public async Task PostLabs_WhenBodyIsMalformed_Returns400WithoutInternalDetails(string body, string contentType)
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        using var request = CreateRequest("POST", Route, Teacher);
        request.Content = new StringContent(body, Encoding.UTF8, contentType);

        using var response = await client.SendAsync(request);
        string responseBody = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.Multiple(() =>
        {
            Assert.That(responseBody, Does.Contain(LabErrorCodes.InvalidBody));
            Assert.That(responseBody, Does.Not.Contain("Exception"));
            Assert.That(responseBody, Does.Not.Contain(" at "));
        });
        Assert.That(await StoredCountAsync(factory), Is.EqualTo(0));
    }

    [Test]
    public async Task PostLabs_WhenClientSendsOwnId_ServerIgnoresItAndAssignsNewId()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        Guid clientSuppliedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid clientSuppliedCreator = Guid.Parse("22222222-2222-2222-2222-222222222222");

        using var response = await client.SendAsync(CreateJsonRequest("POST", Route, Teacher, new
        {
            id = clientSuppliedId,
            title = "RBAC i kontrola pristupa",
            courseId = CourseId,
            createdBy = clientSuppliedCreator,
            status = "Archived",
            createdAt = "2000-01-01T00:00:00Z",
        }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await response.Content.ReadFromJsonAsync<LaboratoryResponse>();
        Assert.That(created, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(created!.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(created.Id, Is.Not.EqualTo(clientSuppliedId));
            Assert.That(created.CreatedBy, Is.Not.EqualTo(clientSuppliedCreator));
            Assert.That(created.Status, Is.EqualTo("Draft"));
            Assert.That(created.CreatedAt.Year, Is.Not.EqualTo(2000));
            Assert.That(created.Title, Is.EqualTo("RBAC i kontrola pristupa"));
        });

        // Klijentski ID nije zavrsio ni u skladistu.
        var repository = factory.Services.GetRequiredService<ILaboratoryRepository>();
        Assert.That(await repository.GetByIdAsync(clientSuppliedId), Is.Null);
        Assert.That(await repository.GetByIdAsync(created!.Id), Is.Not.Null);
    }

    [Test]
    public async Task PostLabs_TwoCreatesWithSameTitle_GetDifferentIds()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        LaboratoryResponse first = await CreateLabAsync(client, Teacher, "Isti naslov");
        LaboratoryResponse second = await CreateLabAsync(client, Teacher, "Isti naslov");

        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
    }

    [Test]
    public async Task PostLabs_ThroughAliasRoute_UsesSameChecks()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        using var teacher = await client.SendAsync(CreateJsonRequest("POST", "/api/labs", Teacher, new { title = "Preko aliasa", courseId = CourseId }));
        using var student = await client.SendAsync(CreateJsonRequest("POST", "/api/labs", Student, new { title = "Preko aliasa", courseId = CourseId }));

        Assert.Multiple(() =>
        {
            Assert.That(teacher.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(teacher.Headers.Location?.OriginalString, Does.StartWith("/api/laboratories/"));
            Assert.That(student.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    // ---------- GET /{id}: citanje ----------

    [Test]
    public async Task GetLabById_WhenOwnerReads_Returns200WithSameData()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Analiza logova");

        using var response = await client.SendAsync(CreateRequest("GET", $"{Route}{created.Id}", Teacher));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var read = await response.Content.ReadFromJsonAsync<LaboratoryResponse>();
        Assert.That(read, Is.EqualTo(created));
    }

    [Test]
    public async Task GetLabById_WhenIdDoesNotExist_Returns404WithoutInternalDetails()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        Guid missingId = Guid.NewGuid();

        using var response = await client.SendAsync(CreateRequest("GET", $"{Route}{missingId}", Teacher));
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain(LabErrorCodes.NotFound));
            Assert.That(body, Does.Not.Contain(missingId.ToString()));
            Assert.That(body, Does.Not.Contain("Exception"));
            Assert.That(body, Does.Not.Contain(" at "));
        });
    }

    [Test]
    public async Task GetLabById_WhenLabBelongsToAnotherTeacher_Returns404SameAsMissing()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Tudji nacrt");

        using var foreign = await client.SendAsync(CreateRequest("GET", $"{Route}{created.Id}", OtherTeacher));
        using var missing = await client.SendAsync(CreateRequest("GET", $"{Route}{Guid.NewGuid()}", OtherTeacher));
        string foreignBody = await foreign.Content.ReadAsStringAsync();
        string missingBody = await missing.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(foreignBody, Is.EqualTo(missingBody));
            Assert.That(foreignBody, Does.Not.Contain("Tudji nacrt"));
            Assert.That(foreignBody, Does.Not.Contain(created.Id.ToString()));
        });
    }

    [Test]
    public async Task GetLabById_WhenStudentReads_Returns403WithoutContent()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Nacrt nije za studente");

        using var response = await client.SendAsync(CreateRequest("GET", $"{Route}{created.Id}", Student));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("Nacrt nije za studente"));
    }

    [Test]
    public async Task GetLabById_WhenAnonymous_Returns401()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Bez identiteta");

        using var response = await client.SendAsync(CreateRequest("GET", $"{Route}{created.Id}", null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("not-a-guid")]
    [TestCase("00000000-0000-0000-0000-00000000000Z")]
    [TestCase("1%27%20OR%20%271%27%3D%271")]
    public async Task GetLabById_WhenIdIsNotAGuid_Returns404(string rawId)
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateRequest("GET", $"{Route}{rawId}", Teacher));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // ---------- GET /{id}: provera konteksta pri otvaranju (LAB-03) ----------
    // Student ne prolazi RequireLabAccess(Read), pa su studentski slucajevi u LaboratoryServiceTests.

    [Test]
    public async Task GetLabById_WhenCourseTeacherOpens_Returns200WithCourse()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Otvaranje na kursu");

        using var response = await client.SendAsync(CreateRequest("GET", $"{Route}{created.Id}", Teacher));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var read = await response.Content.ReadFromJsonAsync<LaboratoryResponse>();
        Assert.That(read!.CourseId, Is.EqualTo(CourseId));
    }

    [Test]
    public async Task GetLabById_WhenTeacherOfOtherCourseOpens_Returns404SameAsMissing()
    {
        // jelena prolazi globalnu proveru (Teacher), ali je nastavnica na drugom kursu.
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        factory.Services.GetRequiredService<InMemoryCourseMembership>()
            .Set(OtherCourseId, UserGuid("jelena"), CourseRole.Teacher);
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Lab drugog kursa");

        using var foreign = await client.SendAsync(CreateRequest("GET", $"{Route}{created.Id}", OtherTeacher));
        using var missing = await client.SendAsync(CreateRequest("GET", $"{Route}{Guid.NewGuid()}", OtherTeacher));
        string foreignBody = await foreign.Content.ReadAsStringAsync();
        string missingBody = await missing.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(missing.StatusCode, Is.EqualTo(foreign.StatusCode));
            Assert.That(foreignBody, Is.EqualTo(missingBody));
            Assert.That(foreignBody, Does.Not.Contain("Lab drugog kursa"));
            Assert.That(foreignBody, Does.Not.Contain(created.Id.ToString()));
            Assert.That(foreignBody, Does.Not.Contain(CourseId.ToString()));
        });
    }

    // ---------- PATCH /{id}/title: preimenovanje ----------

    [Test]
    public async Task PatchLabTitle_WhenTeacherRenamesLab_UpdatesTitleAndKeepsSameId()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Stari naslov");

        using var patch = await client.SendAsync(CreateJsonRequest("PATCH", $"{Route}{created.Id}/title", Teacher, new { title = "Novi naslov" }));

        Assert.That(patch.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updated = await patch.Content.ReadFromJsonAsync<LaboratoryResponse>();
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated, Is.EqualTo(created with { Title = "Novi naslov" }));

        // Izmena je sacuvana: ponovno citanje vraca novi naslov pod istim ID-em.
        var read = await client.SendAsync(CreateRequest("GET", $"{Route}{created.Id}", Teacher));
        Assert.That(await read.Content.ReadFromJsonAsync<LaboratoryResponse>(), Is.EqualTo(updated));
    }

    [Test]
    public async Task PatchLabTitle_RenamedTwice_IdStaysTheSame()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Verzija 1");

        foreach (string title in new[] { "Verzija 2", "Verzija 3" })
        {
            using var patch = await client.SendAsync(CreateJsonRequest("PATCH", $"{Route}{created.Id}/title", Teacher, new { title }));
            var updated = await patch.Content.ReadFromJsonAsync<LaboratoryResponse>();
            Assert.That(updated!.Id, Is.EqualTo(created.Id));
        }

        Assert.That(await StoredCountAsync(factory), Is.EqualTo(1));
    }

    [Test]
    public async Task PatchLabTitle_WhenStudentRenames_Returns403AndKeepsTitle()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Originalni naslov");

        using var patch = await client.SendAsync(CreateJsonRequest("PATCH", $"{Route}{created.Id}/title", Student, new { title = "Hakovano" }));

        Assert.That(patch.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await StoredTitleAsync(factory, created.Id), Is.EqualTo("Originalni naslov"));
    }

    [Test]
    public async Task PatchLabTitle_WhenAnotherTeacherRenames_Returns404AndKeepsTitle()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Originalni naslov");

        using var patch = await client.SendAsync(CreateJsonRequest("PATCH", $"{Route}{created.Id}/title", OtherTeacher, new { title = "Tudja izmena" }));

        Assert.That(patch.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await StoredTitleAsync(factory, created.Id), Is.EqualTo("Originalni naslov"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task PatchLabTitle_WhenTitleIsInvalid_Returns400AndKeepsTitle(string? invalidTitle)
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Originalni naslov");

        using var patch = await client.SendAsync(CreateJsonRequest("PATCH", $"{Route}{created.Id}/title", Teacher, new { title = invalidTitle }));

        Assert.That(patch.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await patch.Content.ReadAsStringAsync(), Does.Contain(LabErrorCodes.InvalidTitle));
        Assert.That(await StoredTitleAsync(factory, created.Id), Is.EqualTo("Originalni naslov"));
    }

    [Test]
    public async Task PatchLabTitle_WhenLabDoesNotExist_Returns404()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();

        using var patch = await client.SendAsync(CreateJsonRequest("PATCH", $"{Route}{Guid.NewGuid()}/title", Teacher, new { title = "Novi naslov" }));

        Assert.That(patch.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await StoredCountAsync(factory), Is.EqualTo(0));
    }

    [Test]
    public async Task PatchLabTitle_CannotChangeIdCreatorOrCourseThroughBody()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        LaboratoryResponse created = await CreateLabAsync(client, Teacher, "Stari naslov");

        using var patch = await client.SendAsync(CreateJsonRequest("PATCH", $"{Route}{created.Id}/title", Teacher, new
        {
            id = Guid.NewGuid(),
            courseId = OtherCourseId,
            createdBy = Guid.NewGuid(),
            status = "Archived",
            title = "Novi naslov",
        }));

        var updated = await patch.Content.ReadFromJsonAsync<LaboratoryResponse>();
        Assert.That(updated, Is.EqualTo(created with { Title = "Novi naslov" }));
    }

    // ---------- Neocekivana greska ----------

    [Test]
    public async Task WhenStorageFails_Returns500WithoutInternalDetails()
    {
        using var factory = new LaboratoriesHostFactory("Development", services =>
            services.AddSingleton<ILaboratoryRepository, FailingRepository>());
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateJsonRequest("POST", Route, Teacher, new { title = "Pad skladista", courseId = CourseId }));
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Not.Contain(FailingRepository.SecretDetail));
            Assert.That(body, Does.Not.Contain("Exception"));
            Assert.That(body, Does.Not.Contain(" at "));
            Assert.That(body, Does.Contain("traceId"));
        });
    }

    // ---------- Pomocne metode ----------

    private static async Task<LaboratoryResponse> CreateLabAsync(HttpClient client, string user, string title)
    {
        using var response = await client.SendAsync(CreateJsonRequest("POST", Route, user, new { title, courseId = CourseId }));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await response.Content.ReadFromJsonAsync<LaboratoryResponse>();
        Assert.That(created, Is.Not.Null);
        return created!;
    }

    private static async Task<int> StoredCountAsync(WebApplicationFactory<Program> factory) =>
        (await factory.Services.GetRequiredService<ILaboratoryRepository>().ListAsync()).Count;

    private static async Task<string?> StoredTitleAsync(WebApplicationFactory<Program> factory, Guid id) =>
        (await factory.Services.GetRequiredService<ILaboratoryRepository>().GetByIdAsync(id))?.Title;

    private static HttpRequestMessage CreateRequest(string method, string path, string? testUser)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (testUser is not null)
        {
            request.Headers.Add(TestUserHeader, testUser);
        }

        return request;
    }

    private static HttpRequestMessage CreateJsonRequest(string method, string path, string? testUser, object body)
    {
        HttpRequestMessage request = CreateRequest(method, path, testUser);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private sealed class LaboratoriesHostFactory(
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

        // LAB-03: clanstvo na test kursu za svaki host.
        protected override IHost CreateHost(IHostBuilder builder)
        {
            IHost host = base.CreateHost(builder);
            var membership = host.Services.GetRequiredService<InMemoryCourseMembership>();
            membership.Set(CourseId, UserGuid("marko"), CourseRole.Teacher);
            membership.Set(CourseId, UserGuid("ana"), CourseRole.Student);
            return host;
        }
    }

    // Isti Guid koji LaboratoryService dobija iz X-Test-User zaglavlja (CallerId).
    private static Guid UserGuid(string userId) => CallerId.From(new TestUser(userId))!.Value;

    private sealed class TestUser(string userId) : IIdentityContext
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; } = userId;
        public PlatformRole? Role => null;
    }

    // Skladiste koje uvek baca gresku sa "internim" tekstom koji ne sme da stigne do klijenta.
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
}