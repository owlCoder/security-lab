using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;

using LabVisibilityCourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.Tests.LabVisibility;

// AUT-07 (stavka 4): GET /api/labs/{id}/preview kroz ceo HTTP pipeline.
// Korisnici se prijavljuju sesijskim kolačićem; članstvo na demo kursu dolazi iz Program.cs.
[TestFixture]
public sealed class LabPreviewHttpTests
{
    private static readonly Guid DemoCourse = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherCourse = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid DraftLab = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private const string Teacher = LabPreviewHostFactory.Teacher;
    private const string Student = LabPreviewHostFactory.Student;
    private const string Outsider = LabPreviewHostFactory.Outsider;
    private const string OtherCourseTeacher = LabPreviewHostFactory.OtherCourseTeacher;

    private LabPreviewHostFactory factory = null!;
    private HttpClient client = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        factory = new LabPreviewHostFactory();
        client = factory.CreateClient();

        var services = factory.Services;

        // Nacrt na demo kursu: student ga ne vidi, a nastavnik kursa ga pregleda.
        services.GetRequiredService<InMemoryLabPublicContentReader>().Save(LabPublicContent.Create(
            DraftLab,
            DemoCourse,
            Guid.Parse(Teacher),
            LabVisibilityState.Draft,
            "Nacrt: analiza dnevnika",
            "Pronađi povezane događaje",
            [new PublicTask("t1", 1, "Koji događaj je prvi?", ["A", "B"])]));

        // Nastavnik drugog kursa: aktivan nalog, ali nije na kursu ove laboratorije.
        services.GetRequiredService<InMemoryCourseMembership>()
            .Set(OtherCourse, Guid.Parse(OtherCourseTeacher), LabVisibilityCourseRole.Teacher);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Test]
    public async Task CourseTeacher_GetsDraftPreview()
    {
        using var response = await GetPreviewAsync(DraftLab, Teacher);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.That(root.GetProperty("isPreview").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("state").GetString(), Is.EqualTo("Draft"));

        var view = root.GetProperty("view");
        Assert.That(view.GetProperty("labId").GetGuid(), Is.EqualTo(DraftLab));
        Assert.That(view.GetProperty("title").GetString(), Is.EqualTo("Nacrt: analiza dnevnika"));
        Assert.That(view.GetProperty("tasks").GetArrayLength(), Is.EqualTo(1));
    }

    // Student (i kad je upisan), korisnik van kursa i nastavnik drugog kursa: isti 404.
    [TestCase(Student)]
    [TestCase(Outsider)]
    [TestCase(OtherCourseTeacher)]
    public async Task NotCourseTeacher_Gets404(string user)
    {
        using var response = await GetPreviewAsync(DraftLab, user);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("lab_not_available"));
    }

    [Test]
    public async Task WithoutIdentity_Gets401()
    {
        using var response = await GetPreviewAsync(DraftLab, user: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // Zabranjeno i nepostojeće moraju izgledati isto, da se ne može pogađati koji ID postoji.
    [Test]
    public async Task Forbidden_And_Missing_LookTheSame()
    {
        using var forbidden = await GetPreviewAsync(DraftLab, Student);
        using var missing = await GetPreviewAsync(Guid.NewGuid(), Teacher);

        Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(
            await forbidden.Content.ReadAsStringAsync(),
            Is.EqualTo(await missing.Content.ReadAsStringAsync()));
    }

    // Pregled nacrta ne sme da ostane u kešu, bez obzira na ishod.
    // (Zahtev bez sesije odbija SessionCheckMiddleware pre kontrolera, bez tela odgovora.)
    [TestCase(Teacher)]
    [TestCase(Student)]
    public async Task EveryResponse_IsNotCached(string user)
    {
        using var response = await GetPreviewAsync(DraftLab, user);

        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    private async Task<HttpResponseMessage> GetPreviewAsync(Guid labId, string? user)
    {
        using var request = LabPreviewHostFactory.PreviewRequest(labId, user);
        return await client.SendAsync(request);
    }
}
