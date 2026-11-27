using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Platform.Audit.Contracts;
using Platform.Api.Controllers.Identity;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Identity.Tests.Controllers;

[TestFixture]
[Category("IDN-07/US-2.1")]
public class SessionControllerTests
{
    private Mock<IIdentityContext> _identity = null!;
    private Mock<ISessionManager> _sessionManager = null!;
    private RecordingAuditWriter _auditWriter = null!;
    private StubCorrelationContext _correlation = null!;
    private IdentityAuditProducer _audit = null!;
    private SessionController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _identity = new Mock<IIdentityContext>();
        _sessionManager = new Mock<ISessionManager>();
        _auditWriter = new RecordingAuditWriter();
        _correlation = new StubCorrelationContext("corr-session-test");
        _audit = new IdentityAuditProducer(
            _auditWriter,
            Mock.Of<ITransactionalAuditWriter>(),
            NullLogger<IdentityAuditProducer>.Instance);

        _controller = new SessionController(
            _identity.Object,
            _sessionManager.Object,
            _audit,
            _correlation)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Test]
    public async Task ActiveCaller_ReturnsOwnSessions()
    {
        var userId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var cancellationToken = cancellation.Token;

        var sessions = new List<SessionInfo>
        {
            new(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddHours(-2),
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddHours(6)),
            new(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddMinutes(-2),
                DateTimeOffset.UtcNow.AddHours(7))
        };

        CallerIs(new CallerIdentity(
            true,
            userId,
            AuthenticationLevel.Active,
            Guid.NewGuid()));

        _sessionManager
            .Setup(s => s.ListForUser(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        var result = await _controller.GetMySessions(cancellationToken);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());

        var okResult = (OkObjectResult)result;
        Assert.That(okResult.Value, Is.SameAs(sessions));

        _sessionManager.Verify(
            s => s.ListForUser(userId, cancellationToken),
            Times.Once);

        Assert.That(
            _controller.Response.Headers.CacheControl.ToString(),
            Is.EqualTo("no-store"));
    }

    [Test]
    public async Task ActiveCaller_WithNoSessions_ReturnsEmptyList()
    {
        var userId = Guid.NewGuid();
        IReadOnlyList<SessionInfo> sessions = new List<SessionInfo>();

        CallerIs(new CallerIdentity(
            true,
            userId,
            AuthenticationLevel.Active,
            Guid.NewGuid()));

        _sessionManager
            .Setup(s => s.ListForUser(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        var result = await _controller.GetMySessions(CancellationToken.None);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());

        var okResult = (OkObjectResult)result;
        Assert.That(okResult.Value, Is.SameAs(sessions));
        Assert.That(((IReadOnlyList<SessionInfo>)okResult.Value!).Count, Is.EqualTo(0));
        _sessionManager.Verify(s => s.ListForUser(userId, CancellationToken.None), Times.Once);
        Assert.That(
            _controller.Response.Headers.CacheControl.ToString(),
            Is.EqualTo("no-store"));
    }

    [Test]
    public async Task NoCaller_Returns401()
    {
        CallerIs(null);

        var result = await _controller.GetMySessions(CancellationToken.None);

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
        AssertUnauthenticated((UnauthorizedObjectResult)result);

        _sessionManager.Verify(
            s => s.ListForUser(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task UnauthenticatedCaller_Returns401()
    {
        CallerIs(new CallerIdentity(
            false,
            Guid.NewGuid(),
            AuthenticationLevel.MfaPending,
            Guid.NewGuid()));

        var result = await _controller.GetMySessions(CancellationToken.None);

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
        AssertUnauthenticated((UnauthorizedObjectResult)result);

        _sessionManager.Verify(
            s => s.ListForUser(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task CallerWithoutUserId_Returns401()
    {
        CallerIs(new CallerIdentity(
            true,
            null,
            AuthenticationLevel.Active,
            Guid.NewGuid()));

        var result = await _controller.GetMySessions(CancellationToken.None);

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
        AssertUnauthenticated((UnauthorizedObjectResult)result);

        _sessionManager.Verify(
            s => s.ListForUser(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RevokeSession_OwnedSession_Returns204AndUsesCallerId()
    {
        var userId = Guid.NewGuid();
        var sessionHandle = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var cancellationToken = cancellation.Token;

        CallerIs(new CallerIdentity(
            true,
            userId,
            AuthenticationLevel.Active,
            Guid.NewGuid()));

        _sessionManager
            .Setup(s => s.RevokeSession(userId, sessionHandle, cancellationToken))
            .ReturnsAsync(true);

        var result = await _controller.RevokeSession(sessionHandle, cancellationToken);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        _sessionManager.Verify(
            s => s.RevokeSession(userId, sessionHandle, cancellationToken),
            Times.Once);
        var write = _auditWriter.Single();
        Assert.Multiple(() =>
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthSessionRevoked));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(write.Draft.Resource!.Id, Is.EqualTo(userId.ToString()));
            Assert.That(write.Draft.Details, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["revocationCause"] = "user_request",
                ["revokedHandle"] = sessionHandle.ToString("D")
            }));
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(userId)));
            Assert.That(write.Context.CorrelationId, Is.EqualTo("corr-session-test"));
        });
        Assert.That(
            _controller.Response.Headers.CacheControl.ToString(),
            Is.EqualTo("no-store"));
    }

    [Test]
    public async Task RevokeSession_UnknownOrForeignHandle_ReturnsSame404()
    {
        var userId = Guid.NewGuid();
        var sessionHandle = Guid.NewGuid();

        CallerIs(new CallerIdentity(
            true,
            userId,
            AuthenticationLevel.Active,
            Guid.NewGuid()));

        _sessionManager
            .Setup(s => s.RevokeSession(
                userId,
                sessionHandle,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.RevokeSession(sessionHandle, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        var notFound = (NotFoundObjectResult)result;
        Assert.That(
            notFound.Value!.GetType().GetProperty("code")!.GetValue(notFound.Value),
            Is.EqualTo("session_not_found"));

        _sessionManager.Verify(
            s => s.RevokeSession(userId, sessionHandle, CancellationToken.None),
            Times.Once);
        var write = _auditWriter.Single();
        Assert.Multiple(() =>
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthSessionRevoked));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(write.Draft.Resource!.Id, Is.EqualTo(userId.ToString()));
            Assert.That(write.Draft.Details, Is.EquivalentTo(
                new Dictionary<string, string> { ["reasonCode"] = "not_found" }));
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(userId)));
            Assert.That(write.Context.CorrelationId, Is.EqualTo("corr-session-test"));
        });
        Assert.That(
            _controller.Response.Headers.CacheControl.ToString(),
            Is.EqualTo("no-store"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RevokeSession_UnauthenticatedCaller_Returns401WithoutCallingManager(bool hasUserId)
    {
        CallerIs(new CallerIdentity(
            false,
            hasUserId ? Guid.NewGuid() : null,
            AuthenticationLevel.MfaPending,
            Guid.NewGuid()));

        var result = await _controller.RevokeSession(Guid.NewGuid(), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
        AssertUnauthenticated((UnauthorizedObjectResult)result);
        _sessionManager.Verify(
            s => s.RevokeSession(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.That(_auditWriter.Writes, Is.Empty);
    }

    [Test]
    public async Task RevokeSession_AuthenticatedCallerWithoutUserId_Returns401WithoutCallingManager()
    {
        CallerIs(new CallerIdentity(
            true,
            null,
            AuthenticationLevel.Active,
            Guid.NewGuid()));

        var result = await _controller.RevokeSession(Guid.NewGuid(), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
        AssertUnauthenticated((UnauthorizedObjectResult)result);
        _sessionManager.Verify(
            s => s.RevokeSession(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.That(_auditWriter.Writes, Is.Empty);
    }

    private void CallerIs(CallerIdentity? caller) =>
        _identity.Setup(i => i.Current).Returns(caller);

    private static void AssertUnauthenticated(UnauthorizedObjectResult result)
    {
        var body = result.Value!;
        Assert.That(
            body.GetType().GetProperty("code")!.GetValue(body),
            Is.EqualTo("unauthenticated"));
    }

    private sealed record RecordedAuditWrite(AuditEventDraft Draft, AuditContext Context);

    private sealed class RecordingAuditWriter : ITrustedAuditWriter
    {
        public List<RecordedAuditWrite> Writes { get; } = [];

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            CancellationToken cancellationToken = default)
        {
            Writes.Add(new RecordedAuditWrite(draft, context));
            return ValueTask.FromResult(
                AuditWriteResult.Recorded(Guid.Parse("44444444-4444-4444-4444-444444444444")));
        }

        public RecordedAuditWrite Single() => Writes.Single();
    }

    private sealed class StubCorrelationContext(string? correlationId) : ICorrelationContext
    {
        public string? CorrelationId { get; } = correlationId;
    }
}
