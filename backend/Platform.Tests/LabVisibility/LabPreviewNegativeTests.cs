using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Application.Preview;
using Platform.LabVisibility.Application.StudentView;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Platform.Tests.LabVisibility;

// AUT-07 (stavka 5, negativni test): pregled ne sme da pokaže ništa više od studentskog prikaza.
// Za istu laboratoriju na serveru postoji pravi ključ sa tajnim odgovorom; on nikad ne sme
// da se pojavi u odgovoru pregleda.
[TestFixture]
public sealed class LabPreviewNegativeTests
{
    private const string SecretAnswer = "TAJNI-ODGOVOR-7f3a";
    private const string Teacher = "11111111-1111-1111-1111-111111111111";

    private static readonly Guid DemoCourse = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LabId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private LabPreviewHostFactory factory = null!;
    private HttpClient client = null!;
    private LabPublicContent draft = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        factory = new LabPreviewHostFactory();
        client = factory.CreateClient();

        draft = CreateContent(LabVisibilityState.Draft);
        factory.Services.GetRequiredService<InMemoryLabPublicContentReader>().Save(draft);

        // Ključ postoji na serveru za ovu istu laboratoriju.
        await factory.Services.GetRequiredService<IEvaluatorKeyStore>().SaveAsync(
            EvaluatorKey.Create(draft, [new TaskAnswerKey("t1", SecretAnswer)]));
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        client.Dispose();
        factory.Dispose();
    }

    // Pregled nacrta = studentski prikaz istog sadržaja posle objave (isti projektor).
    [Test]
    public async Task PreviewView_EqualsStudentViewOfSameContentAfterPublish()
    {
        var body = await GetTeacherPreviewBodyAsync();

        var actualView = JsonNode.Parse(body)!["view"];
        var published = CreateContent(LabVisibilityState.Published);
        var expectedView = JsonSerializer.SerializeToNode(
            StudentViewProjector.Project(published),
            JsonSerializerOptions.Web);

        Assert.That(
            JsonNode.DeepEquals(actualView, expectedView),
            Is.True,
            $"Pregled se razlikuje od studentskog prikaza.{Environment.NewLine}Pregled: {actualView}{Environment.NewLine}Student: {expectedView}");
    }

    [Test]
    public async Task PreviewJson_ContainsNoAnswer()
    {
        var body = await GetTeacherPreviewBodyAsync();

        Assert.That(body, Does.Not.Contain(SecretAnswer));
        Assert.That(body, Does.Not.Contain("answer").IgnoreCase);
    }

    // Strukturno: tok pregleda ne može ni da dobije ključ, pa ga ne može ni vratiti.
    [Test]
    public void PreviewStudentView_DoesNotDependOnEvaluatorKey()
    {
        var keyTypes = new[] { typeof(IEvaluatorKeyStore), typeof(EvaluatorKeyReader), typeof(EvaluatorKey) };

        var dependencies = typeof(PreviewStudentView)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        Assert.That(dependencies.Intersect(keyTypes), Is.Empty);
    }

    private async Task<string> GetTeacherPreviewBodyAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/labs/{LabId}/preview");
        request.Headers.Add("X-Stub-User", Teacher);

        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        return await response.Content.ReadAsStringAsync();
    }

    private static LabPublicContent CreateContent(LabVisibilityState state) =>
        LabPublicContent.Create(
            LabId,
            DemoCourse,
            Guid.Parse(Teacher),
            state,
            "Nacrt: SQL injection",
            "Prepoznati ranjiv upit",
            [new PublicTask("t1", 1, "Koji upit je ranjiv?", ["A", "B", "C"])]);
}
