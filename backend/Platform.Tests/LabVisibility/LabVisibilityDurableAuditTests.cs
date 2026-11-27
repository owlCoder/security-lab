using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Contracts;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Composition;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Auditing;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;

using LabVisibilityCourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.Tests.LabVisibility;

// KT2, kartica 2: događaji LabVisibility idu u trajni audit AUD modula (lab.visibility.accessed),
// bez sadržaja i bez odgovora (AUT-04, AUD-03).
public class LabVisibilityDurableAuditTests
{
    private const string SecretAnswer = "SECRET-ANSWER-AUDIT";

    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LabId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TeacherId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private ServiceProvider provider = null!;
    private CapturingTrustedAuditWriter.Store store = null!;
    private CapturingLoggerProvider logs = null!;

    [SetUp]
    public async Task SetUp()
    {
        store = new CapturingTrustedAuditWriter.Store();
        logs = new CapturingLoggerProvider();
        provider = await BuildProviderAsync(withAudit: true);
    }

    [TearDown]
    public async Task TearDown()
    {
        await provider.DisposeAsync();
        logs.Dispose();
    }

    private async Task<ServiceProvider> BuildProviderAsync(bool withAudit)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        if (withAudit)
        {
            services.AddSingleton(store);
            services.AddScoped<ITrustedAuditWriter, CapturingTrustedAuditWriter>();
        }

        services.AddLabVisibility();
        services.AddLabVisibilityAccess();
        services.AddAccessControl();

        var built = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        var content = LabPublicContent.Create(
            LabId,
            CourseId,
            TeacherId,
            LabVisibilityState.Published,
            "Analiza dnevnika",
            "Pronađi povezane događaje",
            [new PublicTask("t1", 1, "Koji događaj je prvi?", ["A", "B"])]);
        built.GetRequiredService<InMemoryLabPublicContentReader>().Save(content);
        await built.GetRequiredService<IEvaluatorKeyStore>().SaveAsync(
            EvaluatorKey.Create(content, [new TaskAnswerKey("t1", SecretAnswer)]));

        var membership = built.GetRequiredService<InMemoryCourseMembership>();
        membership.Set(CourseId, TeacherId, LabVisibilityCourseRole.Teacher);
        membership.Set(CourseId, StudentId, LabVisibilityCourseRole.Student);

        return built;
    }

    private ValueTask<EvaluatorKey?> ReadKeyAs(SubjectRef? subject) =>
        provider.GetRequiredService<EvaluatorKeyReader>()
            .ReadAsync(subject, LabId, "corr-" + Guid.NewGuid().ToString("N"));

    private static SubjectRef Active(Guid id) => new(id.ToString(), AccountState.Active);

    [Test]
    public void WithAuditModule_UsesDurableAudit()
    {
        Assert.That(
            provider.GetRequiredService<ILabVisibilityAudit>(),
            Is.InstanceOf<AuditWriterLabVisibilityAudit>());
    }

    [Test]
    public async Task WithoutAuditModule_FallsBackToLog()
    {
        await using var withoutAudit = await BuildProviderAsync(withAudit: false);

        Assert.That(
            withoutAudit.GetRequiredService<ILabVisibilityAudit>(),
            Is.InstanceOf<LoggerLabVisibilityAudit>());
    }

    [Test]
    public async Task TeacherReadsKey_RecordsSuccessWithUserActor()
    {
        await ReadKeyAs(Active(TeacherId));

        var (draft, context) = store.Records.Single();
        Assert.Multiple(() =>
        {
            Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.LabVisibilityAccessed));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(draft.Details!["action"], Is.EqualTo(LabVisibilityActions.KeyRead));
            Assert.That(draft.Details!["laboratoryId"], Is.EqualTo(LabId.ToString()));
            Assert.That(draft.Details!.ContainsKey("reasonCode"), Is.False);
            Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(TeacherId)));
        });
    }

    // Negativni test: odbijeno čitanje ključa ostaje odbijeno i upisuje se u trajni audit.
    [Test]
    public async Task StudentReadsKey_IsDeniedAndRecordedAsDenied()
    {
        var key = await ReadKeyAs(Active(StudentId));

        var (draft, context) = store.Records.Single();
        Assert.Multiple(() =>
        {
            Assert.That(key, Is.Null);
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(draft.Details!["reasonCode"], Is.EqualTo("forbidden"));
            Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(StudentId)));
        });
    }

    [Test]
    public async Task AnonymousReadsKey_IsRecordedWithAnonymousActor()
    {
        await ReadKeyAs(null);

        var (draft, context) = store.Records.Single();
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
        Assert.That(context.Actor, Is.EqualTo(AuditActor.Anonymous));
    }

    [Test]
    public async Task EvaluatorReadsKey_IsRecordedAsServiceActor()
    {
        await ReadKeyAs(new SubjectRef(EvaluatorPrincipal.Id, AccountState.Active));

        var (draft, context) = store.Records.Single();
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
        Assert.That(context.Actor, Is.EqualTo(AuditActor.ForService(EvaluatorPrincipal.ServiceActorId)));
    }

    // Negativni test: ni zapis ni kontekst ne sadrže odgovor, ni kad je čitanje dozvoljeno.
    [Test]
    public async Task RecordedAudit_NeverContainsAnswer()
    {
        await ReadKeyAs(Active(TeacherId));
        await ReadKeyAs(Active(StudentId));

        foreach (var (draft, context) in store.Records)
        {
            var serialized = JsonSerializer.Serialize(new { draft, context });
            Assert.That(serialized, Does.Not.Contain(SecretAnswer));
        }
    }

    // Svaki naš ishod (stari i KT2) daje zapis koji AUD katalog prihvata.
    [TestCase("Allow", null)]
    [TestCase("allowed", null)]
    [TestCase("Deny", null)]
    [TestCase("denied", null)]
    [TestCase("NotFound", null)]
    [TestCase("not_found", null)]
    [TestCase("Saved", null)]
    [TestCase("Published", null)]
    [TestCase("Invalid", null)]
    [TestCase("Deny", "33333333-3333-3333-3333-333333333333")]
    [TestCase("Allow", EvaluatorPrincipal.Id)]
    public void Mapping_MatchesAuditCatalog(string outcome, string? actorId)
    {
        var auditEvent = new LabVisibilityAuditEvent(
            LabVisibilityActions.KeyRead,
            LabId,
            actorId ?? TeacherId.ToString(),
            outcome,
            "0HN7ABC:00000001");

        var draft = LabVisibilityAuditMapping.ToDraft(auditEvent);
        var context = LabVisibilityAuditMapping.ToContext(auditEvent);

        Assert.That(AuditEventCatalog.TryGet(draft.EventType, out var schema), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(schema!.ActorsByOutcome.ContainsKey(draft.Outcome), Is.True);
            Assert.That(schema.ActorsByOutcome[draft.Outcome], Does.Contain(context.Actor.Kind));
            Assert.That(draft.Details!.Keys, Is.SubsetOf(schema.Details.Keys));
            Assert.That(schema.RequiredDetails!, Is.SubsetOf(draft.Details.Keys));
            Assert.That(draft.Details.TryGetValue("reasonCode", out var reason),
                Is.EqualTo(draft.Outcome != AuditOutcome.Success));
            if (reason is not null)
            {
                Assert.That(reason, Does.Match("^[a-z][a-z0-9_]{0,31}$"));
            }

            Assert.That(context.CorrelationId, Does.Match("^[A-Za-z0-9._-]{1,64}$"));
        });
    }

    // Svaki poznat ishod ima fiksan reasonCode; tekst ishoda iz koda nikad ne ide u zapis.
    [TestCase("Allow", AuditOutcome.Success, null)]
    [TestCase("allowed", AuditOutcome.Success, null)]
    [TestCase("Saved", AuditOutcome.Success, null)]
    [TestCase("Deny", AuditOutcome.Denied, "forbidden")]
    [TestCase("denied", AuditOutcome.Denied, "forbidden")]
    [TestCase("Unauthenticated", AuditOutcome.Denied, "unauthenticated")]
    [TestCase("Published", AuditOutcome.Denied, "lab_published")]
    [TestCase("NotFound", AuditOutcome.Failure, "not_found")]
    [TestCase("not_found", AuditOutcome.Failure, "not_found")]
    [TestCase("Invalid", AuditOutcome.Failure, "invalid")]
    public void KnownOutcome_MapsToFixedReasonCode(string outcome, AuditOutcome expected, string? reasonCode)
    {
        var draft = LabVisibilityAuditMapping.ToDraft(
            new LabVisibilityAuditEvent(LabVisibilityActions.KeyRead, LabId, TeacherId.ToString(), outcome, "corr-1"));

        Assert.Multiple(() =>
        {
            Assert.That(draft.Outcome, Is.EqualTo(expected));
            Assert.That(draft.Details!.TryGetValue("reasonCode", out var reason) ? reason : null, Is.EqualTo(reasonCode));
        });
    }

    // Negativni test: ishod koji prolazi stari regex (mala slova, cifre, '_') ali nije poznat
    // ne prenosi se u reasonCode, jer oblik stringa ne garantuje da u njemu nema poverljivog podatka.
    [TestCase("secret_answer_42")]
    [TestCase("SecretAnswer42")]
    [TestCase("ok")]
    [TestCase("success")]
    [TestCase("")]
    [TestCase(null)]
    public void UnexpectedOutcome_IsFailureWithFixedCode_AndNeverCopied(string? outcome)
    {
        var draft = LabVisibilityAuditMapping.ToDraft(
            new LabVisibilityAuditEvent(LabVisibilityActions.KeyRead, LabId, TeacherId.ToString(), outcome!, "corr-1"));

        Assert.Multiple(() =>
        {
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(draft.Details!["reasonCode"], Is.EqualTo("unknown_outcome"));
            Assert.That(JsonSerializer.Serialize(draft), Does.Not.Contain("secret").IgnoreCase);
        });
    }

    // Svaka od četiri radnje modula prolazi; ništa drugo.
    [TestCase(LabVisibilityActions.StudentView)]
    [TestCase(LabVisibilityActions.Preview)]
    [TestCase(LabVisibilityActions.KeyRead)]
    [TestCase(LabVisibilityActions.KeyWrite)]
    public void KnownAction_IsWritten(string action)
    {
        var draft = LabVisibilityAuditMapping.ToDraft(
            new LabVisibilityAuditEvent(action, LabId, TeacherId.ToString(), "Allow", "corr-1"));

        Assert.That(draft.Details!["action"], Is.EqualTo(action));
    }

    // Negativni test: nepoznata radnja (i ona koja liči na pravu) ne ide u AUD,
    // a alarm ne sadrži njen tekst.
    [TestCase("lab.answers.dump")]
    [TestCase("lab.key.read.secret_answer_42")]
    [TestCase("LAB.KEY.READ")]
    [TestCase(LabVisibilityActions.Entry)]
    [TestCase("")]
    public async Task UnknownAction_IsNotWritten_AndAlarmHidesIt(string action)
    {
        var audit = provider.GetRequiredService<ILabVisibilityAudit>();

        await audit.WriteAsync(new LabVisibilityAuditEvent(action, LabId, TeacherId.ToString(), "Allow", "corr-1"));

        Assert.Multiple(() =>
        {
            Assert.That(store.Records, Is.Empty);
            Assert.That(
                () => LabVisibilityAuditMapping.ToDraft(
                    new LabVisibilityAuditEvent(action, LabId, TeacherId.ToString(), "Allow", "corr-1")),
                Throws.ArgumentException);
            var alarm = logs.Entries.Single(entry => entry.EventId == AuditWriterLabVisibilityAudit.AuditLost.Id);
            Assert.That(alarm.Message, Does.Contain("UnknownAction"));
            if (action.Length > 0)
            {
                Assert.That(alarm.Message, Does.Not.Contain(action));
            }
        });
    }

    // Negativni test: kad AUD ne sačuva zapis, odluka se ne menja, a alarm u logu je bez odgovora.
    [Test]
    public async Task AuditUnavailable_KeepsDecisionAndRaisesAlarmWithoutAnswer()
    {
        store.Result = AuditWriteResult.Unavailable();

        var teacherKey = await ReadKeyAs(Active(TeacherId));
        var studentKey = await ReadKeyAs(Active(StudentId));

        Assert.Multiple(() =>
        {
            Assert.That(teacherKey, Is.Not.Null);
            Assert.That(studentKey, Is.Null);
            Assert.That(
                logs.Entries.Count(entry => entry.EventId == AuditWriterLabVisibilityAudit.AuditLost.Id),
                Is.EqualTo(2));
            Assert.That(logs.Entries.Select(entry => entry.Message), Has.None.Contains(SecretAnswer));
        });
    }

    private sealed class CapturingTrustedAuditWriter(CapturingTrustedAuditWriter.Store store) : ITrustedAuditWriter
    {
        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            CancellationToken cancellationToken = default)
        {
            store.Records.Enqueue((draft, context));
            return ValueTask.FromResult(store.Result);
        }

        public sealed class Store
        {
            public ConcurrentQueue<(AuditEventDraft Draft, AuditContext Context)> Records { get; } = new();

            public AuditWriteResult Result { get; set; } = AuditWriteResult.Recorded(Guid.NewGuid());
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(int EventId, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<(int EventId, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((eventId.Id, formatter(state, exception)));
        }
    }
}
