using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;

namespace Platform.Audit.Tests;

// S-07: filter normalization and limits of the course audit query. Every refused case is a 400 in S-14.
[TestFixture]
public sealed class CourseAuditQueryNormalizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid CourseK = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Test]
    public void Normalize_WithOnlyCourse_UsesDefaultsAndLastSevenDays()
    {
        var normalized = Valid(new CourseAuditQuery(CourseK));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(normalized.CourseId, Is.EqualTo(CourseK));
            Assert.That(normalized.Page, Is.EqualTo(1));
            Assert.That(normalized.PageSize, Is.EqualTo(50));
            Assert.That(normalized.Offset, Is.Zero);
            Assert.That(normalized.From, Is.EqualTo(Now.AddDays(-7)));
            Assert.That(normalized.To, Is.EqualTo(Now));
            Assert.That(normalized.EventType, Is.Null);
            Assert.That(normalized.Outcome, Is.Null);
            Assert.That(normalized.ActorId, Is.Null);
            Assert.That(normalized.CorrelationId, Is.Null);
        }
    }

    [Test]
    public void Normalize_WithEmptyStrings_TreatsThemAsNotSet()
    {
        var normalized = Valid(new CourseAuditQuery(CourseK, "", "", "", "", "", "", "", ""));

        Assert.That(normalized, Is.EqualTo(Valid(new CourseAuditQuery(CourseK))));
    }

    [Test]
    public void Normalize_WithEmptyCourse_IsRefused()
    {
        AssertSingleError(new CourseAuditQuery(Guid.Empty), CourseAuditQueryField.CourseId,
            CourseAuditQueryErrorCode.Required);
    }

    [Test]
    public void Normalize_WithAllFilters_ParsesEachValue()
    {
        var actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var normalized = Valid(new CourseAuditQuery(CourseK, AuditEventTypes.CourseMemberRoleChanged, "denied",
            actor.ToString(), "2026-11-01T00:00:00Z", "2026-11-03T00:00:00Z", "corr-0001", "3", "20"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(normalized.EventType, Is.EqualTo(AuditEventTypes.CourseMemberRoleChanged));
            Assert.That(normalized.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(normalized.ActorId, Is.EqualTo(actor));
            Assert.That(normalized.CorrelationId, Is.EqualTo("corr-0001"));
            Assert.That(normalized.Page, Is.EqualTo(3));
            Assert.That(normalized.PageSize, Is.EqualTo(20));
            Assert.That(normalized.Offset, Is.EqualTo(40));
        }
    }

    [TestCase("1", 1)]
    [TestCase("100", 100)]
    [TestCase("50", 50)]
    public void Normalize_WithPageSizeInRange_Accepts(string pageSize, int expected)
    {
        Assert.That(Valid(new CourseAuditQuery(CourseK, PageSize: pageSize)).PageSize, Is.EqualTo(expected));
    }

    [TestCase("0", CourseAuditQueryErrorCode.OutOfRange)]
    [TestCase("101", CourseAuditQueryErrorCode.OutOfRange)]
    [TestCase("-1", CourseAuditQueryErrorCode.InvalidFormat)]
    [TestCase("1e2", CourseAuditQueryErrorCode.InvalidFormat)]
    [TestCase(" 10", CourseAuditQueryErrorCode.InvalidFormat)]
    [TestCase("9999999", CourseAuditQueryErrorCode.InvalidFormat)]
    [TestCase("10\n", CourseAuditQueryErrorCode.InvalidFormat)]
    public void Normalize_WithInvalidPageSize_IsRefused(string pageSize, CourseAuditQueryErrorCode code)
    {
        AssertSingleError(new CourseAuditQuery(CourseK, PageSize: pageSize), CourseAuditQueryField.PageSize, code);
    }

    [TestCase("0", CourseAuditQueryErrorCode.OutOfRange)]
    [TestCase("-1", CourseAuditQueryErrorCode.InvalidFormat)]
    [TestCase("abc", CourseAuditQueryErrorCode.InvalidFormat)]
    [TestCase("1.5", CourseAuditQueryErrorCode.InvalidFormat)]
    [TestCase("1\n", CourseAuditQueryErrorCode.InvalidFormat)]
    public void Normalize_WithInvalidPage_IsRefused(string page, CourseAuditQueryErrorCode code)
    {
        AssertSingleError(new CourseAuditQuery(CourseK, Page: page), CourseAuditQueryField.Page, code);
    }

    [Test]
    public void Normalize_WithDeepestAllowedPage_Accepts()
    {
        // (101 - 1) * 100 = 10 000 = MaxOffset.
        Assert.That(Valid(new CourseAuditQuery(CourseK, Page: "101", PageSize: "100")).Offset,
            Is.EqualTo(CourseAuditQueryLimits.MaxOffset));
    }

    [Test]
    public void Normalize_WithPageBeyondMaxOffset_IsRefused()
    {
        AssertSingleError(new CourseAuditQuery(CourseK, Page: "102", PageSize: "100"), CourseAuditQueryField.Page,
            CourseAuditQueryErrorCode.OutOfRange);
    }

    [TestCase("unknown.event")]
    [TestCase("AUTH.LOGIN")]
    [TestCase("auth.login ")]
    [TestCase("audit.events.%")]
    public void Normalize_WithTypeNotInCatalog_IsRefused(string type)
    {
        AssertSingleError(new CourseAuditQuery(CourseK, Type: type), CourseAuditQueryField.Type,
            CourseAuditQueryErrorCode.UnknownValue);
    }

    [Test]
    public void Normalize_AcceptsEveryCatalogType()
    {
        foreach (var type in AuditEventCatalog.Schemas.Keys)
            Assert.That(Valid(new CourseAuditQuery(CourseK, Type: type)).EventType, Is.EqualTo(type));
    }

    [TestCase("success", AuditOutcome.Success)]
    [TestCase("denied", AuditOutcome.Denied)]
    [TestCase("failure", AuditOutcome.Failure)]
    public void Normalize_WithKnownOutcome_Accepts(string outcome, AuditOutcome expected)
    {
        Assert.That(Valid(new CourseAuditQuery(CourseK, Outcome: outcome)).Outcome, Is.EqualTo(expected));
    }

    [TestCase("Success")]
    [TestCase("0")]
    [TestCase("1")]
    [TestCase("allowed")]
    public void Normalize_WithUnknownOutcome_IsRefused(string outcome)
    {
        AssertSingleError(new CourseAuditQuery(CourseK, Outcome: outcome), CourseAuditQueryField.Outcome,
            CourseAuditQueryErrorCode.UnknownValue);
    }

    [TestCase("00000000-0000-0000-0000-000000000000")]
    [TestCase("11111111111111111111111111111111")]
    [TestCase("{11111111-1111-1111-1111-111111111111}")]
    [TestCase("not-a-guid")]
    [TestCase("1 OR 1=1")]
    public void Normalize_WithInvalidActorId_IsRefused(string actorId)
    {
        AssertSingleError(new CourseAuditQuery(CourseK, ActorId: actorId), CourseAuditQueryField.ActorId,
            CourseAuditQueryErrorCode.InvalidFormat);
    }

    [Test]
    public void Normalize_WithCorrelationIdOf64Characters_Accepts()
    {
        var id = new string('a', 64);
        Assert.That(Valid(new CourseAuditQuery(CourseK, CorrelationId: id)).CorrelationId, Is.EqualTo(id));
    }

    [TestCase("a b")]
    [TestCase("x'--")]
    [TestCase("<script>")]
    [TestCase("ć")]
    [TestCase("corr-0001\n")]
    public void Normalize_WithInvalidCorrelationId_IsRefused(string correlationId)
    {
        AssertSingleError(new CourseAuditQuery(CourseK, CorrelationId: correlationId),
            CourseAuditQueryField.CorrelationId, CourseAuditQueryErrorCode.InvalidFormat);
    }

    [Test]
    public void Normalize_WithCorrelationIdOf65Characters_IsRefused()
    {
        AssertSingleError(new CourseAuditQuery(CourseK, CorrelationId: new string('a', 65)),
            CourseAuditQueryField.CorrelationId, CourseAuditQueryErrorCode.InvalidFormat);
    }

    [Test]
    public void Normalize_WithOffsetTimes_ConvertsToUtc()
    {
        var normalized = Valid(new CourseAuditQuery(CourseK,
            From: "2026-11-01T01:00:00+01:00", To: "2026-11-02T00:00:00.5-02:00"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(normalized.From, Is.EqualTo(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
            Assert.That(normalized.From.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(normalized.To,
                Is.EqualTo(new DateTimeOffset(2026, 11, 2, 2, 0, 0, 500, TimeSpan.Zero)));
            Assert.That(normalized.To.Offset, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [TestCase("2026-11-01")]
    [TestCase("2026-11-01T00:00:00")]
    [TestCase("2026-11-01 00:00:00Z")]
    [TestCase("01.11.2026")]
    [TestCase("2026-13-01T00:00:00Z")]
    [TestCase("2026-11-01T00:00:00+0100")]
    [TestCase("1730419200")]
    [TestCase("2026-11-01T00:00:00Z\n")]
    public void Normalize_WithTimeWithoutValidIsoZone_IsRefused(string from)
    {
        AssertSingleError(new CourseAuditQuery(CourseK, From: from), CourseAuditQueryField.From,
            CourseAuditQueryErrorCode.InvalidFormat);
    }

    [Test]
    public void Normalize_WithOverlongTime_IsRefused()
    {
        AssertSingleError(new CourseAuditQuery(CourseK, To: "2026-11-01T00:00:00.1234567+01:00" + new string('0', 10)),
            CourseAuditQueryField.To, CourseAuditQueryErrorCode.InvalidFormat);
    }

    [Test]
    public void Normalize_WithOnlyFrom_UsesSevenDayWindowAfterIt()
    {
        var normalized = Valid(new CourseAuditQuery(CourseK, From: "2026-10-01T00:00:00Z"));

        Assert.That(normalized.From, Is.EqualTo(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.That(normalized.To, Is.EqualTo(new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void Normalize_WithOnlyTo_UsesSevenDayWindowBeforeIt()
    {
        var normalized = Valid(new CourseAuditQuery(CourseK, To: "2026-10-08T00:00:00Z"));

        Assert.That(normalized.From, Is.EqualTo(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.That(normalized.To, Is.EqualTo(new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero)));
    }

    [TestCase("9999-12-31T00:00:00Z", null)]
    [TestCase(null, "0001-01-01T00:00:00Z")]
    [TestCase(null, "0001-01-07T23:59:59Z")]
    public void Normalize_WithOneSidedWindowOutsideCalendar_IsRefusedWithoutException(string? from, string? to)
    {
        AssertSingleError(new CourseAuditQuery(CourseK, From: from, To: to), CourseAuditQueryField.Range,
            CourseAuditQueryErrorCode.OutOfRange);
    }

    [Test]
    public void Normalize_WithExactly31Days_Accepts()
    {
        var normalized = Valid(new CourseAuditQuery(CourseK, From: "2026-10-01T00:00:00Z", To: "2026-11-01T00:00:00Z"));

        Assert.That(normalized.To - normalized.From, Is.EqualTo(CourseAuditQueryLimits.MaxRange));
    }

    [TestCase("2026-10-01T00:00:00Z", "2026-11-01T00:00:00.001Z")]
    [TestCase("2026-11-01T00:00:00Z", "2026-11-01T00:00:00Z")]
    [TestCase("2026-11-02T00:00:00Z", "2026-11-01T00:00:00Z")]
    [TestCase("2026-11-01T01:00:00+01:00", "2026-11-01T00:00:00Z")]
    public void Normalize_WithEmptyReversedOrTooLongRange_IsRefused(string from, string to)
    {
        AssertSingleError(new CourseAuditQuery(CourseK, From: from, To: to), CourseAuditQueryField.Range,
            CourseAuditQueryErrorCode.OutOfRange);
    }

    [Test]
    public void Normalize_WithSeveralInvalidFilters_ReportsEachWithoutValues()
    {
        const string marker = "synthetic-marker";
        var errors = Normalizer().Normalize(new CourseAuditQuery(CourseK, marker, marker, marker, marker, marker,
            marker + " ", marker, marker), out var normalized);

        Assert.That(normalized, Is.Null);
        Assert.That(errors.Select(error => error.Field), Is.EquivalentTo(new[]
        {
            CourseAuditQueryField.Type, CourseAuditQueryField.Outcome, CourseAuditQueryField.ActorId,
            CourseAuditQueryField.From, CourseAuditQueryField.To, CourseAuditQueryField.CorrelationId,
            CourseAuditQueryField.Page, CourseAuditQueryField.PageSize
        }));
        Assert.That(string.Join(";", errors), Does.Not.Contain(marker));
    }

    internal static CourseAuditQueryNormalizer Normalizer() => new(new FixedTimeProvider(Now));

    private static NormalizedCourseAuditQuery Valid(CourseAuditQuery query)
    {
        var errors = Normalizer().Normalize(query, out var normalized);

        Assert.That(errors, Is.Empty);
        return normalized!;
    }

    private static void AssertSingleError(
        CourseAuditQuery query, CourseAuditQueryField field, CourseAuditQueryErrorCode code)
    {
        var errors = Normalizer().Normalize(query, out var normalized);

        Assert.That(normalized, Is.Null);
        Assert.That(errors, Is.EqualTo(new[] { new CourseAuditQueryError(field, code) }));
    }

    internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
