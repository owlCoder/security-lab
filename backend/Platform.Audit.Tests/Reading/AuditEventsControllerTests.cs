using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Platform.Api.Contracts.Audit;
using Platform.Api.Controllers.Auditing;
using Platform.Audit.Contracts;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests.Reading;

[TestFixture]
[Property("RequirementId", "AUD-06")]
public sealed class AuditEventsControllerTests
{
    private FakeReader _reader = null!;
    private FakeActors _actors = null!;
    private FakeCorrelation _correlation = null!;
    private AuditEventsController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _reader = new FakeReader();
        _actors = new FakeActors(AuditActor.ForUser(UserId));
        _correlation = new FakeCorrelation("corr-0001");
        _controller = new AuditEventsController(_reader, _actors, _correlation)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Test]
    public async Task List_Ok_MapsPage_AndForbidsCaching()
    {
        var id = Guid.NewGuid();
        _reader.Result = AuditReadResult.Ok(new AuditEventPage(
            [new AuditEventView(id, DateTimeOffset.UnixEpoch, AuditActorKind.User, UserId,
                AuditEventTypes.CourseMemberRoleChanged, AuditOutcome.Success, CourseId,
                AuditResourceTypes.Account, TargetAccountId.ToString(), "corr-0001",
                new Dictionary<string, string> { ["newRole"] = "assistant" })],
            "next"));

        var result = await _controller.List(CourseId, null, null, null, null, null, null, CancellationToken.None);

        var body = (AuditEventsPageResponse)((OkObjectResult)result).Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.NextCursor, Is.EqualTo("next"));
            Assert.That(body.Items.Single().Id, Is.EqualTo(id));
            Assert.That(body.Items.Single().Actor, Is.EqualTo(new AuditActorResponse("user", UserId)));
            Assert.That(body.Items.Single().Outcome, Is.EqualTo("success"));
            Assert.That(_controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(_reader.LastCourseId, Is.EqualTo(CourseId));
            Assert.That(_reader.LastViewer, Is.EqualTo(new AuditContext(AuditActor.ForUser(UserId), "corr-0001")));
        }
    }

    [Test]
    public async Task List_PassesParsedFiltersToTheReader()
    {
        await _controller.List(CourseId, "auth.login", "Denied", "2026-11-01T00:00:00Z",
            "2026-11-08T10:00:00.5Z", "25", "cur", CancellationToken.None);

        var query = _reader.LastQuery!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.EventType, Is.EqualTo("auth.login"));
            Assert.That(query.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(query.From, Is.EqualTo(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
            Assert.That(query.To!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(query.Limit, Is.EqualTo(25));
            Assert.That(query.Cursor, Is.EqualTo("cur"));
        }
    }

    [TestCase("1", null, null, null)]
    [TestCase("nonsense", null, null, null)]
    [TestCase(null, "2026-11-01T00:00:00+02:00", null, null)]
    [TestCase(null, "2026-11-01", null, null)]
    [TestCase(null, "2026-11-01T00:00:00", null, null)]
    [TestCase(null, null, "yesterday", null)]
    [TestCase(null, null, null, "-5")]
    [TestCase(null, null, null, "ten")]
    public async Task List_WhenAParameterIsMalformed_Returns400_WithoutCallingTheReader(
        string? outcome, string? from, string? to, string? limit)
    {
        var result = await _controller.List(CourseId, null, outcome, from, to, limit, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
            Assert.That(_reader.Calls, Is.Zero);
        }
    }

    [Test]
    public async Task List_WhenReaderSaysInvalid_Returns400()
    {
        _reader.Result = AuditReadResult.InvalidQuery();

        var result = await _controller.List(CourseId, "no.such.type", null, null, null, null, null, CancellationToken.None);

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task List_WhenViewCannotBeRecorded_Returns503_WithoutEvents()
    {
        _reader.Result = AuditReadResult.Unavailable();

        var result = await _controller.List(CourseId, null, null, null, null, null, null, CancellationToken.None);

        var objectResult = (ObjectResult)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(objectResult.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
            Assert.That(objectResult.Value, Is.Not.InstanceOf<AuditEventsPageResponse>());
        }
    }

    [Test]
    public async Task List_WithoutAConfirmedUser_Returns401_AndNeverReads()
    {
        foreach (var actor in new AuditActor?[] { AuditActor.Anonymous, AuditActor.ForService(ServiceId), null })
        {
            _actors.Actor = actor;

            var result = await _controller.List(CourseId, null, null, null, null, null, null, CancellationToken.None);

            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        }

        Assert.That(_reader.Calls, Is.Zero);
    }

    [Test]
    public async Task List_WithoutServerCorrelationId_Returns401_AndNeverReads()
    {
        _correlation.Value = null;

        var result = await _controller.List(CourseId, null, null, null, null, null, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(_reader.Calls, Is.Zero);
        }
    }

    private sealed class FakeReader : IAuditCourseReader
    {
        public AuditReadResult Result { get; set; } = AuditReadResult.Ok(new AuditEventPage([], null));
        public int Calls { get; private set; }
        public Guid? LastCourseId { get; private set; }
        public AuditContext? LastViewer { get; private set; }
        public AuditEventQuery? LastQuery { get; private set; }

        public ValueTask<AuditReadResult> ReadCourseEventsAsync(
            Guid courseId, AuditContext viewer, AuditEventQuery query, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastCourseId = courseId;
            LastViewer = viewer;
            LastQuery = query;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class FakeActors(AuditActor? actor) : IAuditActorSource
    {
        public AuditActor? Actor { get; set; } = actor;
        public AuditActor? GetCurrentActor() => Actor;
    }

    private sealed class FakeCorrelation(string? value) : ICorrelationContext
    {
        public string? Value { get; set; } = value;
        public string? CorrelationId => Value;
    }
}
