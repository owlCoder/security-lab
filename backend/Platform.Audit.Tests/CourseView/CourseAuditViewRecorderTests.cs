using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Application.CourseView;
using Platform.Audit.Contracts;

namespace Platform.Audit.Tests.CourseView;

/// <summary>S-14: the <c>audit.events.viewed</c> record and its failure path (D5: 503 before data).</summary>
[TestFixture]
public sealed class CourseAuditViewRecorderTests
{
    private static readonly Guid Viewer = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CourseK = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset From = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);
    private const string Correlation = "s14-view-0001";

    private static CourseAuditViewSummary Summary(
        string? type = AuditEventTypes.AccessDenied,
        AuditOutcome? outcome = AuditOutcome.Denied,
        int count = 3) =>
        new(Viewer, CourseK, Correlation, type, outcome, From, To, count);

    private static (CourseAuditViewRecorder Recorder, ValidatingTrustedAuditWriter Writer,
        CourseViewCapturingLogger<CourseAuditViewRecorder> Logger) Create()
    {
        var writer = new ValidatingTrustedAuditWriter();
        var logger = new CourseViewCapturingLogger<CourseAuditViewRecorder>();
        return (new CourseAuditViewRecorder(writer, logger), writer, logger);
    }

    [Test]
    public void Draft_WithAllFilters_IsAcceptedByTheRealValidator()
    {
        Assert.That(CourseAuditViewRecorder.TryCreate(Summary(), out var draft, out var context), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(new AuditDraftValidator().Validate(draft, context), Is.EqualTo(AuditRejectionCode.None));
            Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.AuditEventsViewed));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(draft.CourseId, Is.EqualTo(CourseK));
            Assert.That(draft.Resource, Is.EqualTo(new AuditResourceRef(AuditResourceTypes.Course, CourseK.ToString("D"))));
            Assert.That(context, Is.EqualTo(new AuditContext(AuditActor.ForUser(Viewer), Correlation)));
            Assert.That(draft.Details, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["filterType"] = "access.denied",
                ["filterOutcome"] = "denied",
                ["filterFrom"] = "2026-11-01T00:00:00.0000000Z",
                ["filterTo"] = "2026-11-08T00:00:00.0000000Z",
                ["resultCount"] = "3"
            }));
        });
    }

    [Test]
    public void Draft_WithoutTypeAndOutcome_HasOnlyRangeAndCount()
    {
        Assert.That(CourseAuditViewRecorder.TryCreate(Summary(type: null, outcome: null, count: 0), out var draft, out var context), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(draft.Details!.Keys, Is.EquivalentTo(new[] { "filterFrom", "filterTo", "resultCount" }));
            Assert.That(draft.Details["resultCount"], Is.EqualTo("0"));
            Assert.That(new AuditDraftValidator().Validate(draft, context), Is.EqualTo(AuditRejectionCode.None));
        });
    }

    [Test]
    public void Draft_StoresTheRangeInUtc()
    {
        var summary = Summary() with
        {
            From = new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(1)),
            To = new DateTimeOffset(2026, 11, 1, 2, 30, 0, 123, TimeSpan.FromHours(1))
        };

        Assert.That(CourseAuditViewRecorder.TryCreate(summary, out var draft, out _), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(draft.Details!["filterFrom"], Is.EqualTo("2026-11-01T00:30:00.0000000Z"));
            Assert.That(draft.Details["filterTo"], Is.EqualTo("2026-11-01T01:30:00.1230000Z"));
        });
    }

    private static IEnumerable<TestCaseData> InvalidSummaries()
    {
        yield return new TestCaseData(Summary() with { ActorId = Guid.Empty }).SetName("TryCreate_RefusesEmptyViewer");
        yield return new TestCaseData(Summary() with { CourseId = Guid.Empty }).SetName("TryCreate_RefusesEmptyCourse");
        yield return new TestCaseData(Summary() with { From = To }).SetName("TryCreate_RefusesEmptyRange");
        yield return new TestCaseData(Summary(count: -1)).SetName("TryCreate_RefusesNegativeCount");
        yield return new TestCaseData(Summary(type: "x.unknown")).SetName("TryCreate_RefusesTypeOutsideTheCatalog");
        yield return new TestCaseData(Summary(outcome: (AuditOutcome)42)).SetName("TryCreate_RefusesUndefinedOutcome");
        yield return new TestCaseData(Summary() with { CorrelationId = "<script>alert(1)</script>" })
            .SetName("TryCreate_RefusesHostileCorrelationId");
    }

    // The summary type is internal, so the case is passed as object.
    [TestCaseSource(nameof(InvalidSummaries))]
    public async Task InvalidSummary_IsNotWritten_AndReportsFailure(object testCase)
    {
        var summary = (CourseAuditViewSummary)testCase;
        var (recorder, writer, logger) = Create();

        var created = CourseAuditViewRecorder.TryCreate(summary, out _, out _);
        var recorded = await recorder.RecordAsync(summary);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.False);
            Assert.That(recorded, Is.False);
            Assert.That(writer.Calls, Is.Zero);
            Assert.That(logger.Entries.Single().EventId.Id, Is.EqualTo(CourseAuditViewRecorder.AlarmEventId));
        });
    }

    [Test]
    public async Task Recorded_ReturnsTrue_WithExactlyOneWrite()
    {
        var (recorder, writer, logger) = Create();

        var recorded = await recorder.RecordAsync(Summary());

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.True);
            Assert.That(writer.Written, Has.Count.EqualTo(1));
            Assert.That(logger.Entries, Is.Empty);
        });
    }

    [Test]
    public async Task Unavailable_ReturnsFalse_AndRaisesTheAlarm()
    {
        var (recorder, writer, logger) = Create();
        writer.Handler = (_, _, _) => ValueTask.FromResult(AuditWriteResult.Unavailable());

        Assert.That(await recorder.RecordAsync(Summary()), Is.False);

        var entry = logger.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(entry.EventId.Id, Is.EqualTo(CourseAuditViewRecorder.AlarmEventId));
            Assert.That(entry.Text, Is.EqualTo(
                $"audit.events.viewed.failure reason=unavailable code=Unavailable correlationId={Correlation}"));
        });
    }

    [Test]
    public async Task Rejected_ReturnsFalse_AndLogsOnlyTheRejectionCode()
    {
        var (recorder, _, logger) = Create();

        // Above the catalog Count limit (100 000): the real validator rejects it.
        Assert.That(await recorder.RecordAsync(Summary(count: 100_001)), Is.False);

        Assert.That(logger.Entries.Single().Text, Is.EqualTo(
            $"audit.events.viewed.failure reason=rejected code=InvalidDetailValue correlationId={Correlation}"));
    }

    [Test]
    public async Task WriterThatThrows_ReturnsFalse_WithoutCopyingTheMessage()
    {
        var (recorder, writer, logger) = Create();
        writer.Handler = (_, _, _) => throw new InvalidOperationException("Host=db;Password=s3cret;token=abc");

        Assert.That(await recorder.RecordAsync(Summary()), Is.False);

        var text = logger.Entries.Single().Text;
        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(
                $"audit.events.viewed.failure reason=failed code=InvalidOperationException correlationId={Correlation}"));
            Assert.That(text, Does.Not.Contain("s3cret").And.Not.Contain("Password").And.Not.Contain("token"));
        });
    }

    [Test]
    public async Task Alarm_ForAHostileCorrelationId_UsesThePlaceholder()
    {
        var (recorder, _, logger) = Create();

        await recorder.RecordAsync(Summary() with { CorrelationId = "evil\r\nforged=1" });

        Assert.That(logger.Entries.Single().Text, Does.EndWith(
            $"correlationId={CourseAuditViewRecorder.InvalidCorrelationPlaceholder}").And.Not.Contain("forged"));
    }

    [Test]
    public void Alarm_NeverContainsViewerCourseOrFilterValues()
    {
        var (recorder, writer, logger) = Create();
        writer.Handler = (_, _, _) => ValueTask.FromResult(AuditWriteResult.Unavailable());

        Assert.That(async () => await recorder.RecordAsync(Summary()), Throws.Nothing);

        var text = logger.Entries.Single().Text;
        Assert.That(text, Does.Not.Contain(Viewer.ToString()).And.Not.Contain(CourseK.ToString())
            .And.Not.Contain("access.denied").And.Not.Contain("2026-11"));
    }

    [Test]
    public void CallerCancellation_Propagates()
    {
        var (recorder, writer, _) = Create();
        writer.Handler = (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(AuditWriteResult.Recorded(Guid.NewGuid()));
        };
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await recorder.RecordAsync(Summary(), cancelled.Token));
    }
}
