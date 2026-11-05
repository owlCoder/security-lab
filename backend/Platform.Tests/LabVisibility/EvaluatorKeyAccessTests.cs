using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Composition;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Platform.Tests.LabVisibility;

// AUT-04 / AUD-03: ključ čita samo evaluator i nastavnik kursa, a u logu nikad nema odgovora.
public class EvaluatorKeyAccessTests
{
    private const string SecretAnswer = "SECRET-ANSWER-42";

    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherCourseId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid LabId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TeacherId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OutsiderId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherTeacherId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private ServiceProvider provider = null!;
    private CapturingLoggerProvider logs = null!;

    [SetUp]
    public async Task SetUp()
    {
        logs = new CapturingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddAccessControl();
        services.AddLabVisibility();
        provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        provider.RegisterLabVisibilityAccess();

        var content = LabPublicContent.Create(
            LabId,
            CourseId,
            TeacherId,
            LabVisibilityState.Published,
            "Analiza dnevnika",
            "Pronađi povezane događaje",
            [new PublicTask("t1", 1, "Koji događaj je prvi?", ["A", "B"])]);
        provider.GetRequiredService<InMemoryLabPublicContentReader>().Save(content);

        await provider.GetRequiredService<IEvaluatorKeyStore>().SaveAsync(
            EvaluatorKey.Create(content, [new TaskAnswerKey("t1", SecretAnswer)]));

        var membership = provider.GetRequiredService<InMemoryCourseMembership>();
        membership.Set(CourseId, TeacherId, CourseRole.Teacher);
        membership.Set(CourseId, StudentId, CourseRole.Student);
        membership.Set(OtherCourseId, OtherTeacherId, CourseRole.Teacher);
    }

    [TearDown]
    public async Task TearDown()
    {
        await provider.DisposeAsync();
        logs.Dispose();
    }

    private ValueTask<EvaluatorKey?> ReadAs(SubjectRef? subject) =>
        provider.GetRequiredService<EvaluatorKeyReader>()
            .ReadAsync(subject, LabId, Guid.NewGuid().ToString("N"));

    private static SubjectRef User(Guid id, AccountState state = AccountState.Active) =>
        new(id.ToString(), state);

    [Test]
    public async Task Evaluator_CanReadKey()
    {
        var key = await ReadAs(new SubjectRef(EvaluatorPrincipal.Id, AccountState.Active));

        Assert.That(key, Is.Not.Null);
        Assert.That(key!.Answers.Single().ExpectedAnswer, Is.EqualTo(SecretAnswer));
    }

    [Test]
    public async Task CourseTeacher_CanReadKey()
    {
        Assert.That(await ReadAs(User(TeacherId)), Is.Not.Null);
    }

    [Test]
    public async Task EnrolledStudent_IsDenied()
    {
        Assert.That(await ReadAs(User(StudentId)), Is.Null);
    }

    [Test]
    public async Task UserOutsideCourse_IsDenied()
    {
        Assert.That(await ReadAs(User(OutsiderId)), Is.Null);
    }

    [Test]
    public async Task TeacherOfAnotherCourse_IsDenied()
    {
        Assert.That(await ReadAs(User(OtherTeacherId)), Is.Null);
    }

    [Test]
    public async Task BlockedTeacher_IsDenied()
    {
        Assert.That(await ReadAs(User(TeacherId, AccountState.Blocked)), Is.Null);
    }

    [Test]
    public async Task Anonymous_IsDenied()
    {
        Assert.That(await ReadAs(null), Is.Null);
    }

    [Test]
    public async Task DeniedRead_IsAudited()
    {
        await ReadAs(User(StudentId));

        Assert.That(logs.Messages, Has.Some.Contains("action=lab.key.read")
            .And.Contains($"actor={StudentId}")
            .And.Contains("outcome=Deny"));
    }

    [Test]
    public async Task AllowedRead_IsAuditedWithoutAnswer()
    {
        await ReadAs(new SubjectRef(EvaluatorPrincipal.Id, AccountState.Active));

        Assert.That(logs.Messages, Has.Some.Contains("outcome=Allow"));
        Assert.That(logs.Messages, Has.None.Contains(SecretAnswer));
    }

    // Arhitekturni test: kontroler ne sme ni da dobije pristup ključu.
    [Test]
    public void NoController_DependsOnKeyStoreOrReader()
    {
        var forbidden = new[] { typeof(IEvaluatorKeyStore), typeof(EvaluatorKeyReader) };

        var offenders = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters())
                .Concat(t.GetMethods().SelectMany(m => m.GetParameters()))
                .Where(p => forbidden.Contains(p.ParameterType))
                .Select(p => $"{t.Name}.{p.Member.Name}({p.Name})"))
            .ToList();

        Assert.That(offenders, Is.Empty);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }
}
