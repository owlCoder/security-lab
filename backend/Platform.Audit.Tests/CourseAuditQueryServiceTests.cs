using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;

namespace Platform.Audit.Tests;

// S-07: the query service without a database (fake reader).
[TestFixture]
public sealed class CourseAuditQueryServiceTests
{
    private static readonly Guid CourseK = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Test]
    public async Task Query_WithInvalidFilter_DoesNotReadStorage()
    {
        var reader = new FakeReader(0);

        var result = await Service(reader).QueryAsync(new CourseAuditQuery(CourseK, PageSize: "0"));

        Assert.That(result.Status, Is.EqualTo(CourseAuditQueryStatus.Invalid));
        Assert.That(result.Page, Is.Null);
        Assert.That(reader.Calls, Is.Empty);
    }

    [Test]
    public async Task Query_WithEmptyCourse_IsRefusedWithoutStorageCall()
    {
        var reader = new FakeReader(0);

        var result = await Service(reader).QueryAsync(new CourseAuditQuery(Guid.Empty));

        Assert.That(result.Errors, Is.EqualTo(new[]
        {
            new CourseAuditQueryError(CourseAuditQueryField.CourseId, CourseAuditQueryErrorCode.Required)
        }));
        Assert.That(reader.Calls, Is.Empty);
    }

    [Test]
    public async Task Query_PassesCourseAndNormalizedFiltersToReader()
    {
        var reader = new FakeReader(0);

        await Service(reader).QueryAsync(new CourseAuditQuery(CourseK, Outcome: "denied", Page: "2", PageSize: "10"));

        var call = reader.Calls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.CourseId, Is.EqualTo(CourseK));
            Assert.That(call.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(call.Offset, Is.EqualTo(10));
            Assert.That(call.PageSize, Is.EqualTo(10));
        }
    }

    [Test]
    public async Task Query_WhenReaderReturnsExtraRow_TrimsItAndReportsNextPage()
    {
        var result = await Service(new FakeReader(11)).QueryAsync(new CourseAuditQuery(CourseK, PageSize: "10"));

        Assert.That(result.Status, Is.EqualTo(CourseAuditQueryStatus.Ok));
        Assert.That(result.Page!.Items, Has.Count.EqualTo(10));
        Assert.That(result.Page.HasNextPage, Is.True);
    }

    [TestCase(0)]
    [TestCase(10)]
    public async Task Query_WhenReaderReturnsAtMostPageSize_HasNoNextPage(int rows)
    {
        var result = await Service(new FakeReader(rows)).QueryAsync(new CourseAuditQuery(CourseK, PageSize: "10"));

        Assert.That(result.Page!.Items, Has.Count.EqualTo(rows));
        Assert.That(result.Page.HasNextPage, Is.False);
        Assert.That(result.Page.CourseId, Is.EqualTo(CourseK));
    }

    [Test]
    public async Task Query_WhenStorageFails_ReturnsUnavailableWithoutDetails()
    {
        var reader = new FakeReader(0) { Failure = new InvalidOperationException("Host=db;Password=synthetic-marker") };

        var result = await Service(reader).QueryAsync(new CourseAuditQuery(CourseK));

        Assert.That(result.Status, Is.EqualTo(CourseAuditQueryStatus.Unavailable));
        Assert.That(result.Page, Is.Null);
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.ToString(), Does.Not.Contain("synthetic-marker"));
    }

    [Test]
    public void Query_WhenCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reader = new FakeReader(0) { Failure = new OperationCanceledException(cancellation.Token) };

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Service(reader).QueryAsync(new CourseAuditQuery(CourseK), cancellation.Token));
    }

    private static CourseAuditQueryService Service(FakeReader reader) =>
        new(CourseAuditQueryNormalizerTests.Normalizer(), reader);

    private sealed class FakeReader(int rows) : ICourseAuditEventReader
    {
        public List<NormalizedCourseAuditQuery> Calls { get; } = [];

        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<CourseAuditEventDto>> ReadPageAsync(
            NormalizedCourseAuditQuery query, CancellationToken cancellationToken = default)
        {
            Calls.Add(query);
            if (Failure is not null)
                throw Failure;

            IReadOnlyList<CourseAuditEventDto> result = Enumerable.Range(0, rows)
                .Select(index => new CourseAuditEventDto(Guid.NewGuid(), query.To.AddSeconds(-index - 1),
                    AuditEventTypes.CourseMemberRoleChanged, AuditOutcome.Success, AuditActorKind.User,
                    Guid.NewGuid(), AuditResourceTypes.Account, "acc", "corr-0001"))
                .ToArray();
            return Task.FromResult(result);
        }
    }
}
