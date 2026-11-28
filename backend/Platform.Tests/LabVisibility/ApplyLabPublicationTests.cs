using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.Composition;
using Platform.LabVisibility.Application.Publication;
using Platform.LabVisibility.Composition;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Persistence;
using Platform.LabVisibility.Infrastructure.Publication;

namespace Platform.Tests.LabVisibility;

// AUT-07 (KT2, kartica AUT-07-2): pravila objave i arhiviranja nad ILabPublicationSource.
public sealed class ApplyLabPublicationTests
{
    private const string Secret = "TAJNI-ODGOVOR-PUB-91c2";
    private const string TeacherId = "11111111-1111-1111-1111-111111111111";

    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private ServiceProvider provider = null!;
    private RecordingAudit audit = null!;
    private Guid labId;

    [SetUp]
    public void SetUp()
    {
        audit = new RecordingAudit();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLabVisibility();
        services.AddLabVisibilityAccess();
        services.AddAccessControl();
        services.RemoveAll<ILabVisibilityAudit>();
        services.AddSingleton<ILabVisibilityAudit>(audit);

        provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        labId = Guid.NewGuid();
    }

    [TearDown]
    public async Task TearDown() => await provider.DisposeAsync();

    [Test]
    public async Task DraftWithCompleteKey_PublishedBySource_BecomesPublished_AndIsAudited()
    {
        var content = SaveContent(LabVisibilityState.Draft, "t1", "t2");
        await SaveKeyAsync(content);
        SetSource(LabPublicationStatus.Published);

        var result = await ApplyAsync();

        Assert.That(result.Outcome, Is.EqualTo(LabPublicationOutcome.Published));
        Assert.That(await StateAsync(), Is.EqualTo(LabVisibilityState.Published));
        Assert.That(audit.Events, Has.Count.EqualTo(1));
        Assert.That(audit.Events[0].Action, Is.EqualTo(LabPublicationAuditActions.Publish));
        Assert.That(audit.Events[0].Outcome, Is.EqualTo("success"));
        Assert.That(audit.Events[0].ActorId, Is.EqualTo(TeacherId));
    }

    // Negativni test: objava bez ključa se odbija, laboratorija ostaje nacrt, a pokušaj je u auditu.
    [Test]
    public async Task DraftWithoutKey_IsRejected_StaysDraft_AndIsAudited()
    {
        SaveContent(LabVisibilityState.Draft, "t1");
        SetSource(LabPublicationStatus.Published);

        var result = await ApplyAsync();

        Assert.That(result.Outcome, Is.EqualTo(LabPublicationOutcome.Rejected));
        Assert.That(result.ReasonCode, Is.EqualTo(ApplyLabPublication.KeyIncomplete));
        Assert.That(await StateAsync(), Is.EqualTo(LabVisibilityState.Draft));
        Assert.That(audit.Events, Has.Count.EqualTo(1));
        Assert.That(audit.Events[0].Action, Is.EqualTo(LabPublicationAuditActions.Publish));
        Assert.That(audit.Events[0].Outcome, Is.EqualTo(ApplyLabPublication.KeyIncomplete));
    }

    // Negativni test: ključ je upisan, pa je posle dodat zadatak bez odgovora → ključ više nije potpun.
    [Test]
    public async Task DraftWhoseKeyMissesNewTask_IsRejected()
    {
        var oldContent = SaveContent(LabVisibilityState.Draft, "t1");
        await SaveKeyAsync(oldContent);
        SaveContent(LabVisibilityState.Draft, "t1", "t2");
        SetSource(LabPublicationStatus.Published);

        var result = await ApplyAsync();

        Assert.That(result.ReasonCode, Is.EqualTo(ApplyLabPublication.KeyIncomplete));
        Assert.That(await StateAsync(), Is.EqualTo(LabVisibilityState.Draft));
    }

    [Test]
    public async Task Published_ArchivedBySource_BecomesArchived_AndIsAudited()
    {
        SaveContent(LabVisibilityState.Published, "t1");
        SetSource(LabPublicationStatus.Archived);

        var result = await ApplyAsync();

        Assert.That(result.Outcome, Is.EqualTo(LabPublicationOutcome.Archived));
        Assert.That(await StateAsync(), Is.EqualTo(LabVisibilityState.Archived));
        Assert.That(audit.Events.Single().Action, Is.EqualTo(LabPublicationAuditActions.Archive));
        Assert.That(audit.Events.Single().Outcome, Is.EqualTo("success"));
    }

    // Negativni test: iz arhive nema povratka; nacrt ne može direktno u arhivu.
    [TestCase(LabVisibilityState.Archived, LabPublicationStatus.Published)]
    [TestCase(LabVisibilityState.Published, LabPublicationStatus.Draft)]
    [TestCase(LabVisibilityState.Draft, LabPublicationStatus.Archived)]
    public async Task InvalidTransition_IsRejected_AndStateIsKept(
        LabVisibilityState current,
        LabPublicationStatus reported)
    {
        var content = SaveContent(current, "t1");
        await SaveKeyAsync(content);
        SetSource(reported);

        var result = await ApplyAsync();

        Assert.That(result.Outcome, Is.EqualTo(LabPublicationOutcome.Rejected));
        Assert.That(result.ReasonCode, Is.EqualTo(ApplyLabPublication.InvalidTransition));
        Assert.That(await StateAsync(), Is.EqualTo(current));
        Assert.That(audit.Events.Single().Outcome, Is.EqualTo(ApplyLabPublication.InvalidTransition));
    }

    // Negativni test (stub ugovora): nepoznata laboratorija kod vlasnika objave → ništa se ne menja.
    [Test]
    public async Task LabUnknownToPublicationSource_NothingChanges()
    {
        SaveContent(LabVisibilityState.Draft, "t1");

        var result = await ApplyAsync();

        Assert.That(result.Outcome, Is.EqualTo(LabPublicationOutcome.NotFound));
        Assert.That(await StateAsync(), Is.EqualTo(LabVisibilityState.Draft));
        Assert.That(audit.Events, Is.Empty);
    }

    [Test]
    public async Task SameState_IsUnchanged_AndNotAudited()
    {
        SaveContent(LabVisibilityState.Published, "t1");
        SetSource(LabPublicationStatus.Published);

        var result = await ApplyAsync();

        Assert.That(result.Outcome, Is.EqualTo(LabPublicationOutcome.Unchanged));
        Assert.That(audit.Events, Is.Empty);
    }

    // Uslovna promena (T27): ako stanje nije ono koje je objava očekivala, ništa se ne prepisuje.
    [Test]
    public async Task StateStore_ChangesOnlyFromExpectedState()
    {
        SaveContent(LabVisibilityState.Published, "t1");
        var states = provider.GetRequiredService<ILabPublicationStateStore>();

        var changed = await states.TryChangeStateAsync(labId, LabVisibilityState.Draft, LabVisibilityState.Archived);

        Assert.That(changed, Is.False);
        Assert.That(await StateAsync(), Is.EqualTo(LabVisibilityState.Published));
    }

    // AUD-03: ni rezultat ni audit objave ne nose odgovor iz ključa.
    [Test]
    public async Task PublicationResultAndAudit_NeverContainAnswer()
    {
        var content = SaveContent(LabVisibilityState.Draft, "t1", "t2");
        await SaveKeyAsync(content);
        SetSource(LabPublicationStatus.Published);

        var result = await ApplyAsync();

        Assert.That(result.ToString(), Does.Not.Contain(Secret));
        Assert.That(string.Join("|", audit.Events), Does.Not.Contain(Secret));
    }

    private LabPublicContent SaveContent(LabVisibilityState state, params string[] taskIds)
    {
        var content = LabPublicContent.Create(
            labId,
            CourseId,
            Guid.Parse(TeacherId),
            state,
            "Objava: analiza dnevnika",
            "Pronađi povezane događaje",
            taskIds.Select((id, index) => new PublicTask(id, index + 1, $"Pitanje {id}?", ["A", "B"])).ToArray());

        provider.GetRequiredService<InMemoryLabPublicContentReader>().Save(content);
        return content;
    }

    private ValueTask SaveKeyAsync(LabPublicContent content) =>
        provider.GetRequiredService<IEvaluatorKeyStore>().SaveAsync(
            EvaluatorKey.Create(content, content.Tasks.Select(task => new TaskAnswerKey(task.TaskId, Secret)).ToArray()));

    private void SetSource(LabPublicationStatus status) =>
        provider.GetRequiredService<StubLabPublicationSource>()
            .Set(new LabPublication(labId, Guid.NewGuid(), status, DateTimeOffset.UtcNow));

    private async Task<LabPublicationResult> ApplyAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplyLabPublication>()
            .ExecuteAsync(labId, TeacherId, Guid.NewGuid().ToString("N"));
    }

    private async Task<LabVisibilityState?> StateAsync() =>
        (await provider.GetRequiredService<ILabPublicContentReader>().GetAsync(labId))?.State;

    private sealed class RecordingAudit : ILabVisibilityAudit
    {
        public List<LabVisibilityAuditEvent> Events { get; } = [];

        public ValueTask WriteAsync(LabVisibilityAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return ValueTask.CompletedTask;
        }
    }
}
