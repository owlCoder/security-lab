using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Services.Identity;
using Platform.LabVisibility.Application.Publication;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Persistence;
using Platform.LabVisibility.Infrastructure.Publication;

namespace Platform.Tests.LabVisibility;

// AUT-07 (KT2): ceo tok kroz HTTP. Nastavnik pregleda nacrt, vlasnik objave (stub ugovora)
// objavi laboratoriju, upisan student dobija isti sadržaj kao pregled, bez odgovora;
// posle arhiviranja student dobija 404, a nastavnik i dalje ima pregled.
[TestFixture]
public sealed class LabPublicationFlowHttpTests
{
    private const string Secret = "TAJNI-ODGOVOR-FLOW-5d0e";
    private static readonly Guid DemoCourse = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid FlowLab = Guid.Parse("f1f1f1f1-f1f1-f1f1-f1f1-f1f1f1f1f1f1");

    private LabPreviewHostFactory factory = null!;
    private HttpClient client = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        factory = new LabPreviewHostFactory();
        client = factory.CreateClient();

        var content = LabPublicContent.Create(
            FlowLab,
            DemoCourse,
            Guid.Parse(LabPreviewHostFactory.Teacher),
            LabVisibilityState.Draft,
            "Tok objave: SQL injection",
            "Prepoznati ranjiv upit",
            [
                new PublicTask("t1", 1, "Koji upit je ranjiv?", ["A", "B", "C"]),
                new PublicTask("t2", 2, "Koja zaštita pomaže?", ["Parametri", "Escape"])
            ]);

        factory.Services.GetRequiredService<InMemoryLabPublicContentReader>().Save(content);
        await factory.Services.GetRequiredService<IEvaluatorKeyStore>().SaveAsync(
            EvaluatorKey.Create(content, [new TaskAnswerKey("t1", Secret), new TaskAnswerKey("t2", Secret)]));
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Test]
    public async Task PreviewOfDraft_EqualsStudentViewAfterPublish_AndArchiveHidesLabFromStudent()
    {
        // 1. Pre objave: nastavnik vidi pregled nacrta, student ne vidi ništa.
        var previewBody = await GetOkBodyAsync("preview", LabPreviewHostFactory.Teacher);
        var previewView = JsonNode.Parse(previewBody)!["view"];
        Assert.That(await GetStatusAsync("student-view", LabPreviewHostFactory.Student), Is.EqualTo(HttpStatusCode.NotFound));

        // 2. Vlasnik objave javi Published, a mi primenimo objavu (ključ je potpun).
        Assert.That((await PublishAsync(LabPublicationStatus.Published)).Outcome, Is.EqualTo(LabPublicationOutcome.Published));

        // 3. Student vidi tačno isto što je nastavnik video u pregledu nacrta, bez odgovora.
        var studentBody = await GetOkBodyAsync("student-view", LabPreviewHostFactory.Student);
        Assert.That(JsonNode.DeepEquals(previewView, JsonNode.Parse(studentBody)), Is.True,
            $"Pregled: {previewView}{Environment.NewLine}Student: {studentBody}");
        Assert.That(studentBody, Does.Not.Contain(Secret));
        Assert.That(previewBody, Does.Not.Contain(Secret));

        // 4. Arhiviranje: student dobija 404, nastavnik i dalje ima pregled (stanje Archived).
        Assert.That((await PublishAsync(LabPublicationStatus.Archived)).Outcome, Is.EqualTo(LabPublicationOutcome.Archived));
        Assert.That(await GetStatusAsync("student-view", LabPreviewHostFactory.Student), Is.EqualTo(HttpStatusCode.NotFound));

        var archivedPreview = JsonNode.Parse(await GetOkBodyAsync("preview", LabPreviewHostFactory.Teacher))!;
        Assert.That(archivedPreview["state"]!.GetValue<string>(), Is.EqualTo("Archived"));
    }

    private async Task<LabPublicationResult> PublishAsync(LabPublicationStatus status)
    {
        factory.Services.GetRequiredService<StubLabPublicationSource>()
            .Set(new LabPublication(FlowLab, Guid.NewGuid(), status, DateTimeOffset.UtcNow));

        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplyLabPublication>()
            .ExecuteAsync(FlowLab, LabPreviewHostFactory.Teacher, Guid.NewGuid().ToString("N"));
    }

    private async Task<string> GetOkBodyAsync(string view, string user)
    {
        using var response = await SendAsync(view, user);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), view);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<HttpStatusCode> GetStatusAsync(string view, string user)
    {
        using var response = await SendAsync(view, user);
        return response.StatusCode;
    }

    private async Task<HttpResponseMessage> SendAsync(string view, string user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/labs/{FlowLab:D}/{view}");
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={user}");
        return await client.SendAsync(request);
    }
}
