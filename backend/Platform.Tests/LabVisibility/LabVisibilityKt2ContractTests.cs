using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Composition;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;
using Platform.LabVisibility.Infrastructure.Publication;

using LabVisibilityCourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.Tests.LabVisibility;

// KT2 ugovori (docs/contracts/LabVisibility-KT2.md): upis ključa i stanje objave od AUT-01/REV.
public class LabVisibilityKt2ContractTests
{
    private const string SecretAnswer = "SECRET-ANSWER-KT2";

    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private ServiceProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddLabVisibility();
        services.AddLabVisibilityAccess();
        services.AddAccessControl();
        provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        var reader = provider.GetRequiredService<InMemoryLabPublicContentReader>();
        foreach (var (content, _) in LabVisibilityDemoSeed.Create())
        {
            reader.Save(content);
        }

        provider.GetRequiredService<InMemoryCourseMembership>()
            .Set(LabVisibilityDemoSeed.DemoCourseId, StudentId, LabVisibilityCourseRole.Student);
    }

    [TearDown]
    public async Task TearDown() => await provider.DisposeAsync();

    [Test]
    public void KeyWrite_IsRegisteredAsLabAction()
    {
        Assert.That(LabVisibilityActions.All, Does.Contain(LabVisibilityActions.KeyWrite));
        Assert.That(LabVisibilityActions.KeyWrite, Is.EqualTo("lab.key.write"));
    }

    [TestCase(true, AccessOutcome.Allow)]
    [TestCase(false, AccessOutcome.Deny)]
    public async Task KeyWrite_OnDraft_ChecksTeachersCourse(
        bool teachesLabCourse,
        AccessOutcome expectedOutcome)
    {
        var teacherId = Guid.NewGuid();
        var courseId = teachesLabCourse
            ? LabVisibilityDemoSeed.DemoCourseId
            : Guid.NewGuid();

        provider.GetRequiredService<InMemoryCourseMembership>()
            .Set(courseId, teacherId, LabVisibilityCourseRole.Teacher);

        var decision = await provider.GetRequiredService<IAccessDecision>().DecideAsync(
            new AccessRequest(
                new SubjectRef(teacherId.ToString(), AccountState.Active),
                LabVisibilityActions.KeyWrite,
                new ResourceRef("lab", LabVisibilityDemoSeed.DraftLabId.ToString()),
                Guid.NewGuid().ToString("N")));

        Assert.That(decision.Outcome, Is.EqualTo(expectedOutcome));
    }

    // Negativni test: upisan student nikad ne sme da upiše ključ, ni posle kartice 5.
    [Test]
    public async Task EnrolledStudent_IsDeniedKeyWrite()
    {
        var decision = await provider.GetRequiredService<IAccessDecision>().DecideAsync(
            new AccessRequest(
                new SubjectRef(StudentId.ToString(), AccountState.Active),
                LabVisibilityActions.KeyWrite,
                new ResourceRef("lab", LabVisibilityDemoSeed.DraftLabId.ToString()),
                Guid.NewGuid().ToString("N")));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [TestCase(LabPublicationStatus.Published, true, SaveEvaluatorKeyOutcome.Published)]
    [TestCase(LabPublicationStatus.Published, false, SaveEvaluatorKeyOutcome.NotFound)]
    [TestCase(LabPublicationStatus.Archived, true, SaveEvaluatorKeyOutcome.Published)]
    [TestCase(LabPublicationStatus.Archived, false, SaveEvaluatorKeyOutcome.NotFound)]
    public async Task SaveEvaluatorKey_ClosedLab_ProtectsStateAndKey(
        LabPublicationStatus status,
        bool teachesLabCourse,
        SaveEvaluatorKeyOutcome expectedOutcome)
    {
        var labId = LabVisibilityDemoSeed.DraftLabId;
        var teacherId = Guid.NewGuid();
        var courseId = teachesLabCourse ? LabVisibilityDemoSeed.DemoCourseId : Guid.NewGuid();

        provider.GetRequiredService<InMemoryCourseMembership>()
            .Set(courseId, teacherId, LabVisibilityCourseRole.Teacher);

        // Izvor objave je već ažuriran, a lokalni zapis još prikazuje Draft.
        provider.GetRequiredService<StubLabPublicationSource>()
            .Set(new LabPublication(labId, Guid.NewGuid(), status, DateTimeOffset.UtcNow));

        await using var scope = provider.CreateAsyncScope();
        var save = scope.ServiceProvider.GetRequiredService<ISaveEvaluatorKey>();
        var store = scope.ServiceProvider.GetRequiredService<IEvaluatorKeyStore>();

        var result = await save.ExecuteAsync(
            new SaveEvaluatorKeyCommand(labId, [new EvaluatorKeyAnswerInput("t1", SecretAnswer)]),
            new SubjectRef(teacherId.ToString(), AccountState.Active),
            Guid.NewGuid().ToString("N"));

        Assert.That(await store.GetAsync(labId), Is.Null, "Odbijen upis ne sme da sačuva ključ.");
        Assert.That(result.Outcome, Is.EqualTo(expectedOutcome));
    }

    [TestCase(SaveEvaluatorKeyErrors.UnknownTask)]
    [TestCase(SaveEvaluatorKeyErrors.MissingTask)]
    [TestCase(SaveEvaluatorKeyErrors.DuplicateTask)]
    [TestCase(SaveEvaluatorKeyErrors.EmptyAnswer)]
    [TestCase(SaveEvaluatorKeyErrors.AnswerTooLong)]
    public async Task SaveEvaluatorKey_InvalidInput_PreservesPreviousKey(string expectedCode)
    {
        var labId = LabVisibilityDemoSeed.DraftLabId;
        var teacherId = LabVisibilityDemoSeed.TeacherId;
        var teacher = new SubjectRef(teacherId.ToString(), AccountState.Active);
        provider.GetRequiredService<InMemoryCourseMembership>()
            .Set(LabVisibilityDemoSeed.DemoCourseId, teacherId, LabVisibilityCourseRole.Teacher);

        await using var scope = provider.CreateAsyncScope();
        var save = scope.ServiceProvider.GetRequiredService<ISaveEvaluatorKey>();
        var store = scope.ServiceProvider.GetRequiredService<IEvaluatorKeyStore>();

        const string originalAnswer = "original-answer";
        var first = await save.ExecuteAsync(
            new SaveEvaluatorKeyCommand(labId, [new EvaluatorKeyAnswerInput("t1", originalAnswer)]),
            teacher,
            Guid.NewGuid().ToString("N"));
        Assert.That(first.Outcome, Is.EqualTo(SaveEvaluatorKeyOutcome.Saved));

        var attemptedAnswer = new EvaluatorKeyAnswerInput("t1", SecretAnswer);
        EvaluatorKeyAnswerInput[] invalidAnswers = expectedCode switch
        {
            SaveEvaluatorKeyErrors.UnknownTask => [new("unknown-task", SecretAnswer)],
            SaveEvaluatorKeyErrors.MissingTask => [],
            SaveEvaluatorKeyErrors.DuplicateTask => [attemptedAnswer, attemptedAnswer],
            SaveEvaluatorKeyErrors.EmptyAnswer => [new("t1", "   ")],
            SaveEvaluatorKeyErrors.AnswerTooLong => [new("t1", SecretAnswer.PadRight(
                Platform.LabVisibility.Domain.TaskAnswerKey.MaxExpectedAnswerLength + 1, 'x'))],
            _ => throw new ArgumentOutOfRangeException(nameof(expectedCode))
        };

        var result = await save.ExecuteAsync(
            new SaveEvaluatorKeyCommand(labId, invalidAnswers),
            teacher,
            Guid.NewGuid().ToString("N"));

        Assert.That(result.Outcome, Is.EqualTo(SaveEvaluatorKeyOutcome.Invalid));
        Assert.That(result.ErrorCode, Is.EqualTo(expectedCode));
        Assert.That(result.ToString(), Does.Not.Contain(SecretAnswer));

        var stored = await store.GetAsync(labId);
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.Answers, Has.Count.EqualTo(1));
        Assert.That(stored.Answers[0].TaskId, Is.EqualTo("t1"));
        Assert.That(stored.Answers[0].ExpectedAnswer, Is.EqualTo(originalAnswer));
    }

    private sealed class RecordingWriteAudit : ILabVisibilityAudit
    {
        public List<LabVisibilityAuditEvent> Events { get; } = new();

        public ValueTask WriteAsync(
            LabVisibilityAuditEvent auditEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return ValueTask.CompletedTask;
        }
    }

    [TestCase(true, SaveEvaluatorKeyOutcome.Saved)]
    [TestCase(false, SaveEvaluatorKeyOutcome.NotFound)]
    public async Task SaveEvaluatorKey_AuditsAttemptWithoutAnswer(
        bool isTeacher,
        SaveEvaluatorKeyOutcome expectedOutcome)
    {
        var actorId = isTeacher ? LabVisibilityDemoSeed.TeacherId : StudentId;
        var role = isTeacher ? LabVisibilityCourseRole.Teacher : LabVisibilityCourseRole.Student;
        var labId = LabVisibilityDemoSeed.DraftLabId;
        var correlationId = Guid.NewGuid().ToString("N");

        provider.GetRequiredService<InMemoryCourseMembership>()
            .Set(LabVisibilityDemoSeed.DemoCourseId, actorId, role);

        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var audit = new RecordingWriteAudit();

        var save = new SaveEvaluatorKey(
            services.GetRequiredService<IAccessDecision>(),
            services.GetRequiredService<ILabPublicContentReader>(),
            services.GetRequiredService<ILabPublicationSource>(),
            services.GetRequiredService<IEvaluatorKeyStore>(),
            audit);

        var result = await save.ExecuteAsync(
            new SaveEvaluatorKeyCommand(labId, [new EvaluatorKeyAnswerInput("t1", SecretAnswer)]),
            new SubjectRef(actorId.ToString(), AccountState.Active),
            correlationId);

        Assert.That(result.Outcome, Is.EqualTo(expectedOutcome));
        Assert.That(audit.Events, Has.Count.EqualTo(1));

        var entry = audit.Events[0];
        Assert.That(entry.Action, Is.EqualTo("lab.key.write"));
        Assert.That(entry.LabId, Is.EqualTo(labId));
        Assert.That(entry.ActorId, Is.EqualTo(actorId.ToString()));
        Assert.That(entry.Outcome, Is.EqualTo(expectedOutcome.ToString()));
        Assert.That(entry.CorrelationId, Is.EqualTo(correlationId));

        var auditJson = System.Text.Json.JsonSerializer.Serialize(entry);
        Assert.That(auditJson, Does.Not.Contain(SecretAnswer));
    }

    [Test]
    public async Task SaveEvaluatorKey_TeacherSavesDraftKey()
    {
        var teacherId = LabVisibilityDemoSeed.TeacherId;
        var labId = LabVisibilityDemoSeed.DraftLabId;
        provider.GetRequiredService<InMemoryCourseMembership>()
            .Set(LabVisibilityDemoSeed.DemoCourseId, teacherId, LabVisibilityCourseRole.Teacher);

        await using var scope = provider.CreateAsyncScope();
        var save = scope.ServiceProvider.GetRequiredService<ISaveEvaluatorKey>();
        var store = scope.ServiceProvider.GetRequiredService<IEvaluatorKeyStore>();

        var result = await save.ExecuteAsync(
            new SaveEvaluatorKeyCommand(labId, [new EvaluatorKeyAnswerInput("t1", SecretAnswer)]),
            new SubjectRef(teacherId.ToString(), AccountState.Active),
            Guid.NewGuid().ToString("N"));

        Assert.That(result.Outcome, Is.EqualTo(SaveEvaluatorKeyOutcome.Saved));
        var storedKey = await store.GetAsync(labId);
        Assert.That(storedKey, Is.Not.Null);
        Assert.That(storedKey!.Answers, Has.Count.EqualTo(1));
        Assert.That(storedKey.Answers[0].TaskId, Is.EqualTo("t1"));
        Assert.That(storedKey.Answers[0].ExpectedAnswer, Is.EqualTo(SecretAnswer));
    }

    [Test]
    public async Task SaveEvaluatorKey_IsResolvable_AndDeniesStudent()
    {
        await using var scope = provider.CreateAsyncScope();
        var save = scope.ServiceProvider.GetRequiredService<ISaveEvaluatorKey>();

        var result = await save.ExecuteAsync(
            new SaveEvaluatorKeyCommand(
                LabVisibilityDemoSeed.DraftLabId,
                [new EvaluatorKeyAnswerInput("t1", SecretAnswer)]),
            new SubjectRef(StudentId.ToString(), AccountState.Active),
            Guid.NewGuid().ToString("N"));

        Assert.That(result.Outcome, Is.EqualTo(SaveEvaluatorKeyOutcome.NotFound));
        Assert.That(result.ErrorCode, Is.Null);
    }

    // Negativni test: komanda ne sme da ispiše odgovor ni kroz ToString (log, izuzetak, debugger).
    [Test]
    public void Command_ToString_NeverContainsAnswer()
    {
        var answer = new EvaluatorKeyAnswerInput("t1", SecretAnswer);
        var command = new SaveEvaluatorKeyCommand(LabVisibilityDemoSeed.DraftLabId, [answer]);

        Assert.That(answer.ToString(), Does.Not.Contain(SecretAnswer));
        Assert.That(command.ToString(), Does.Not.Contain(SecretAnswer));
    }

    // Pozitivan odgovor stub-a: demo laboratorije postoje kod vlasnika objave.
    [Test]
    public async Task PublicationStub_ReturnsStateForDemoLabs()
    {
        var source = provider.GetRequiredService<ILabPublicationSource>();

        var published = await source.GetAsync(LabVisibilityDemoSeed.PublishedLabId);
        var draft = await source.GetAsync(LabVisibilityDemoSeed.DraftLabId);

        Assert.That(published?.Status, Is.EqualTo(LabPublicationStatus.Published));
        Assert.That(draft?.Status, Is.EqualTo(LabPublicationStatus.Draft));
    }

    // Negativan odgovor stub-a: nepoznata laboratorija → null.
    [Test]
    public async Task PublicationStub_ReturnsNullForUnknownLab()
    {
        var source = provider.GetRequiredService<ILabPublicationSource>();

        Assert.That(await source.GetAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task PublicationStub_Set_ChangesState()
    {
        var stub = provider.GetRequiredService<StubLabPublicationSource>();
        stub.Set(new LabPublication(
            LabVisibilityDemoSeed.DraftLabId,
            Guid.NewGuid(),
            LabPublicationStatus.Published,
            DateTimeOffset.UnixEpoch));

        var draft = await provider.GetRequiredService<ILabPublicationSource>()
            .GetAsync(LabVisibilityDemoSeed.DraftLabId);

        Assert.That(draft?.Status, Is.EqualTo(LabPublicationStatus.Published));
    }
}