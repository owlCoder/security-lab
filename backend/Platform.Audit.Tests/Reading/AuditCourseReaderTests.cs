using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using static Platform.Audit.Tests.AuditDraftExamples;
using static Platform.Audit.Tests.Reading.ReadingExamples;

namespace Platform.Audit.Tests.Reading;

[TestFixture]
[Property("RequirementId", "AUD-06")]
public sealed class AuditCourseReaderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    private FakeQueryStore _store = null!;
    private FakeTrustedWriter _writer = null!;
    private AuditCourseReader _reader = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new FakeQueryStore();
        _writer = new FakeTrustedWriter();
        _reader = new AuditCourseReader(_store, _writer, NullLogger<AuditCourseReader>.Instance);
    }

    [Test]
    public async Task Read_ReturnsOnlyTheRequestedCourse_NewestFirst_AndRecordsTheView()
    {
        _store.Rows.Add(Event(CourseId, T0));
        _store.Rows.Add(Event(CourseId, T0.AddMinutes(5)));
        _store.Rows.Add(Event(OtherCourseId, T0.AddMinutes(9)));

        var result = await _reader.ReadCourseEventsAsync(CourseId, UserContext, new AuditEventQuery());

        Assert.That(result.Status, Is.EqualTo(AuditReadStatus.Ok));
        var items = result.Page!.Items;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items.All(item => item.CourseId == CourseId), Is.True);
            Assert.That(items[0].OccurredAt, Is.GreaterThan(items[1].OccurredAt));
            Assert.That(result.Page.NextCursor, Is.Null);
            Assert.That(_store.LastCourseId, Is.EqualTo(CourseId));
        }

        var (draft, context) = _writer.Written.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.AuditEventsViewed));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(draft.CourseId, Is.EqualTo(CourseId));
            Assert.That(draft.Resource, Is.EqualTo(new AuditResourceRef(AuditResourceTypes.Course, CourseId.ToString("D"))));
            Assert.That(draft.Details!["resultCount"], Is.EqualTo("2"));
            Assert.That(context, Is.EqualTo(UserContext));
        }
    }

    [Test]
    public async Task Read_RecordsFiltersInAViewEventThatPassesTheRealCatalogValidator()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 11, 8, 12, 30, 15, TimeSpan.Zero).AddTicks(1230000);
        var query = new AuditEventQuery(AuditEventTypes.CourseMemberRoleChanged, AuditOutcome.Denied, from, to, 10);

        await _reader.ReadCourseEventsAsync(CourseId, UserContext, query);

        var (draft, context) = _writer.Written.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(draft.Details!["filterType"], Is.EqualTo("course.member.role.changed"));
            Assert.That(draft.Details["filterOutcome"], Is.EqualTo("denied"));
            Assert.That(draft.Details["filterFrom"], Is.EqualTo("2026-11-01T00:00:00Z"));
            Assert.That(draft.Details["filterTo"], Does.EndWith("Z"));
            Assert.That(new AuditDraftValidator().Validate(draft, context), Is.EqualTo(AuditRejectionCode.None));
        }
    }

    [Test]
    public async Task Read_WhenViewCannotBeRecorded_ReturnsNoData_FailClosed()
    {
        _store.Rows.Add(Event(CourseId, T0));

        foreach (var failure in new[] { AuditWriteResult.Unavailable(), AuditWriteResult.Rejected(AuditRejectionCode.InvalidContext) })
        {
            _writer.Result = failure;
            var result = await _reader.ReadCourseEventsAsync(CourseId, UserContext, new AuditEventQuery());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Status, Is.EqualTo(AuditReadStatus.Unavailable));
                Assert.That(result.Page, Is.Null);
            }
        }
    }

    [Test]
    public async Task Read_WhenWriterThrows_ReturnsUnavailableWithoutData()
    {
        _store.Rows.Add(Event(CourseId, T0));
        _writer.Throws = new InvalidOperationException("db password=hunter2");

        var result = await _reader.ReadCourseEventsAsync(CourseId, UserContext, new AuditEventQuery());

        Assert.That(result, Is.EqualTo(AuditReadResult.Unavailable()));
    }

    [Test]
    public async Task Read_WhenStoreFails_ReturnsUnavailable_AndRecordsNothing()
    {
        _store.Throws = new InvalidOperationException("connection lost");

        var result = await _reader.ReadCourseEventsAsync(CourseId, UserContext, new AuditEventQuery());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuditReadStatus.Unavailable));
            Assert.That(_writer.Written, Is.Empty);
        }
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(AuditReadLimits.MaxLimit + 1)]
    public async Task Read_WhenLimitIsOutOfRange_IsInvalid_AndNothingHappens(int limit)
    {
        var result = await _reader.ReadCourseEventsAsync(CourseId, UserContext, new AuditEventQuery(Limit: limit));

        AssertInvalidWithoutSideEffects(result);
    }

    [Test]
    public async Task Read_WhenFilterIsInvalid_IsInvalid_AndNothingHappens()
    {
        var utc = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
        var queries = new[]
        {
            new AuditEventQuery(EventType: "not.in.catalog"),
            new AuditEventQuery(EventType: "auth.login' OR '1'='1"),
            new AuditEventQuery(Outcome: (AuditOutcome)42),
            new AuditEventQuery(From: new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.FromHours(2))),
            new AuditEventQuery(To: new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.FromHours(-5))),
            new AuditEventQuery(From: utc.AddHours(1), To: utc),
            new AuditEventQuery(Cursor: "garbage"),
            new AuditEventQuery(Cursor: "1_00000000000000000000000000000000"),
        };

        foreach (var query in queries)
        {
            _writer.Written.Clear();
            var result = await _reader.ReadCourseEventsAsync(CourseId, UserContext, query);
            AssertInvalidWithoutSideEffects(result);
        }
    }

    [Test]
    public async Task Read_WhenViewerIsNotAConfirmedUser_ReturnsUnavailable_AndReadsNothing()
    {
        foreach (var viewer in new[] { AnonymousContext, ServiceContext })
        {
            var result = await _reader.ReadCourseEventsAsync(CourseId, viewer, new AuditEventQuery());

            Assert.That(result.Status, Is.EqualTo(AuditReadStatus.Unavailable));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_store.Calls, Is.Zero);
            Assert.That(_writer.Written, Is.Empty);
        }
    }

    [Test]
    public async Task Read_WhenCourseIsEmptyGuid_IsInvalid()
    {
        var result = await _reader.ReadCourseEventsAsync(Guid.Empty, UserContext, new AuditEventQuery());

        AssertInvalidWithoutSideEffects(result);
    }

    [Test]
    public async Task Read_Pages_WithoutGapsOrDuplicates_ThroughTheCursor()
    {
        for (var i = 0; i < 5; i++)
            _store.Rows.Add(Event(CourseId, T0.AddMinutes(i)));

        var first = await _reader.ReadCourseEventsAsync(CourseId, UserContext, new AuditEventQuery(Limit: 2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Page!.Items, Has.Count.EqualTo(2));
            Assert.That(first.Page.NextCursor, Is.Not.Null);
            Assert.That(_store.LastTake, Is.EqualTo(3), "one extra row decides whether a next page exists");
        }

        var cursorOk = AuditCursor.TryParse(first.Page!.NextCursor, out var cursor);
        var last = first.Page.Items[^1];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cursorOk, Is.True);
            Assert.That(cursor.Id, Is.EqualTo(last.Id));
            Assert.That(cursor.OccurredAt, Is.EqualTo(last.OccurredAt));
        }

        var second = await _reader.ReadCourseEventsAsync(
            CourseId, UserContext, new AuditEventQuery(Limit: 2, Cursor: first.Page.NextCursor));

        Assert.That(second.Status, Is.EqualTo(AuditReadStatus.Ok));
        Assert.That(_store.LastFilter!.After, Is.EqualTo(cursor));
    }

    [Test]
    public void Read_WhenRequestIsCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        _store.Throws = new OperationCanceledException(cts.Token);
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _reader.ReadCourseEventsAsync(CourseId, UserContext, new AuditEventQuery(), cts.Token));
    }

    private void AssertInvalidWithoutSideEffects(AuditReadResult result)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuditReadStatus.InvalidQuery));
            Assert.That(result.Page, Is.Null);
            Assert.That(_store.Calls, Is.Zero);
            Assert.That(_writer.Written, Is.Empty);
        }
    }
}

[TestFixture]
public sealed class AuditCursorTests
{
    [Test]
    public void Format_ThenTryParse_RoundTrips()
    {
        var original = new AuditCursor(new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero).AddTicks(1230), Guid.NewGuid());

        var ok = AuditCursor.TryParse(original.Format(), out var parsed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.True);
            Assert.That(parsed, Is.EqualTo(original));
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("_")]
    [TestCase("123_")]
    [TestCase("_00000000000000000000000000000001")]
    [TestCase("-1_00000000000000000000000000000001")]
    [TestCase("abc_00000000000000000000000000000001")]
    [TestCase("123_not-a-guid")]
    [TestCase("123_00000000-0000-0000-0000-000000000001")]
    [TestCase("99999999999999999999_00000000000000000000000000000001")]
    public void TryParse_RejectsMalformedInput(string? value)
    {
        Assert.That(AuditCursor.TryParse(value, out _), Is.False);
    }
}
