using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;
using Platform.Api.Services.Identity;
using Platform.Audit.Application;
using Platform.Audit.Contracts;

namespace Platform.Identity.Tests.Services.Audit;

[TestFixture]
[Category("IDN-05")]
public sealed class MfaLockoutAuditContractTests
{
    private static readonly Guid TargetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly AuditContext AdminContext = new(
        AuditActor.ForUser(Guid.Parse("11111111-1111-1111-1111-111111111111")), "server-unlock-270");
    private static readonly DateTimeOffset End = new(2026, 11, 28, 12, 15, 0, TimeSpan.FromHours(1));

    [Test]
    public async Task MfaThrottled_HttpLimit_RecordsAnonymousDenialWithoutRequestData()
    {
        // Arrange
        AuditEventDraft? capturedDraft = null;
        AuditContext? capturedContext = null;
        var writer = new Mock<ITrustedAuditWriter>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        writer.Setup(w => w.WriteAsync(It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(), cancellation.Token))
            .Callback<AuditEventDraft, AuditContext, CancellationToken>((draft, context, _) =>
            {
                capturedDraft = draft;
                capturedContext = context;
            })
            .Returns(new ValueTask<AuditWriteResult>(AuditWriteResult.Recorded(Guid.NewGuid())));
        var transactional = new Mock<ITransactionalAuditWriter>(MockBehavior.Strict);
        var producer = new IdentityAuditProducer(writer.Object, transactional.Object,
            NullLogger<IdentityAuditProducer>.Instance);

        // Act
        var result = await producer.MfaThrottledAsync("server-mfa-270", cancellation.Token);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsRecorded, Is.True);
            Assert.That(capturedDraft!.EventType, Is.EqualTo(AuditEventTypes.AuthMfaThrottled));
            Assert.That(capturedDraft.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(capturedDraft.Resource, Is.Null);
            Assert.That(capturedDraft.CourseId, Is.Null);
            Assert.That(capturedDraft.Details, Is.Null);
            Assert.That(capturedContext!.Actor, Is.EqualTo(AuditActor.Anonymous));
            Assert.That(capturedContext.CorrelationId, Is.EqualTo("server-mfa-270"));
            Assert.That(new AuditDraftValidator().Validate(capturedDraft, capturedContext), Is.EqualTo(AuditRejectionCode.None));
        });
        writer.VerifyAll();
        transactional.VerifyNoOtherCalls();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MfaThrottled_AuditUnavailable_ReturnsUnavailableWithoutProviderException(bool throws)
    {
        // Arrange
        var writer = new Mock<ITrustedAuditWriter>();
        var setup = writer.Setup(w => w.WriteAsync(It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(),
            It.IsAny<CancellationToken>()));
        if (throws)
            setup.Throws(new InvalidOperationException("provider-sensitive-content"));
        else
            setup.Returns(new ValueTask<AuditWriteResult>(AuditWriteResult.Unavailable()));
        var records = new List<string>();
        var logger = new Mock<ILogger<IdentityAuditProducer>>();
        logger.Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(call =>
            {
                if (call.Arguments[4] is not Delegate formatter)
                    throw new InvalidOperationException("The log formatter is required.");
                records.Add($"{call.Arguments[2]} {formatter.DynamicInvoke(call.Arguments[2], call.Arguments[3])} {call.Arguments[3]}");
            }));
        var producer = new IdentityAuditProducer(writer.Object, Mock.Of<ITransactionalAuditWriter>(),
            logger.Object);

        // Act
        var result = await producer.MfaThrottledAsync("server-mfa-270");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AuditWriteStatus.Unavailable));
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0], Does.Contain(AuditEventTypes.AuthMfaThrottled).And.Contain("server-mfa-270"));
            Assert.That(records, Has.None.Contains("provider-sensitive-content"));
        });
    }

    [TestCase("password", 0, null, "2026-11-28T11:15:00Z")]
    [TestCase("mfa", null, 0, "2026-11-28T11:15:00Z")]
    [TestCase("both", 0, 10, "2026-11-28T11:25:00Z")]
    [TestCase("both", 10, 0, "2026-11-28T11:25:00Z")]
    [TestCase("both", 0, 0, "2026-11-28T11:15:00Z")]
    public void CreateAccountUnlockedDraft_ClearedLockouts_MapsKindAndLatestUtcEnd(
        string kind, int? passwordMinutes, int? mfaMinutes, string expectedEnd)
    {
        // Arrange
        DateTimeOffset? passwordEnd = passwordMinutes is { } p ? End.AddMinutes(p) : null;
        DateTimeOffset? mfaEnd = mfaMinutes is { } m ? End.AddMinutes(m) : null;

        // Act
        var draft = IdentityAuditProducer.CreateAccountUnlockedDraft(TargetId, passwordEnd, mfaEnd);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.AccountUnlocked));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(draft.Resource, Is.EqualTo(new AuditResourceRef(AuditResourceTypes.Account, TargetId.ToString())));
            Assert.That(draft.Details, Is.EquivalentTo(new Dictionary<string, string>
                { ["lockoutKind"] = kind, ["lockoutEnd"] = expectedEnd }));
            Assert.That(new AuditDraftValidator().Validate(draft, AdminContext), Is.EqualTo(AuditRejectionCode.None));
        });
    }

    [Test]
    public void CreateAccountUnlockedDraft_NoClearedLockout_Throws()
    {
        // Arrange
        var target = TargetId;

        // Act & Assert
        Assert.That(() => IdentityAuditProducer.CreateAccountUnlockedDraft(target, null, null),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("passwordLockoutEnd"));
    }

    [Test]
    public void CreateAccountUnlockedDraft_EmptyTarget_Throws()
    {
        // Arrange
        var end = End;

        // Act & Assert
        Assert.That(() => IdentityAuditProducer.CreateAccountUnlockedDraft(Guid.Empty, end, null), Throws.ArgumentException);
    }

    [TestCase("password")]
    [TestCase("mfa")]
    [TestCase("both")]
    [TestCase(null)] // Existing producers without the additive detail remain valid.
    public void Validate_UnlockWithKnownOrAbsentKind_Accepts(string? kind)
    {
        // Arrange
        var details = new Dictionary<string, string> { ["lockoutEnd"] = "2026-11-28T11:15:00Z" };
        if (kind is not null) details["lockoutKind"] = kind;
        var draft = new AuditEventDraft(AuditEventTypes.AccountUnlocked, AuditOutcome.Success,
            Resource: new AuditResourceRef(AuditResourceTypes.Account, TargetId.ToString()), Details: details);

        // Act
        var rejection = new AuditDraftValidator().Validate(draft, AdminContext);

        // Assert
        Assert.That(rejection, Is.EqualTo(AuditRejectionCode.None));
    }

    [TestCase("other", AuditOutcome.Success, AuditRejectionCode.InvalidDetailValue)]
    [TestCase("MFA", AuditOutcome.Success, AuditRejectionCode.InvalidDetailValue)]
    [TestCase("password", AuditOutcome.Denied, AuditRejectionCode.UnknownDetailKey)]
    [TestCase("mfa", AuditOutcome.Failure, AuditRejectionCode.UnknownDetailKey)]
    public void Validate_InvalidUnlockKindOrOutcome_Rejects(string kind, AuditOutcome outcome, AuditRejectionCode expected)
    {
        // Arrange
        var draft = new AuditEventDraft(AuditEventTypes.AccountUnlocked, outcome,
            Resource: new AuditResourceRef(AuditResourceTypes.Account, TargetId.ToString()),
            Details: new Dictionary<string, string> { ["lockoutKind"] = kind });

        // Act
        var rejection = new AuditDraftValidator().Validate(draft, AdminContext);

        // Assert
        Assert.That(rejection, Is.EqualTo(expected));
    }

    [TestCase("user", AuditRejectionCode.InvalidContext)]
    [TestCase("resource", AuditRejectionCode.InvalidResource)]
    [TestCase("course", AuditRejectionCode.CourseNotAllowed)]
    [TestCase("details", AuditRejectionCode.UnknownDetailKey)]
    [TestCase("success", AuditRejectionCode.OutcomeNotAllowed)]
    public void Validate_MfaThrottleWithUnexpectedData_Rejects(string change, AuditRejectionCode expected)
    {
        // Arrange
        var draft = new AuditEventDraft(AuditEventTypes.AuthMfaThrottled, AuditOutcome.Denied);
        var context = new AuditContext(AuditActor.Anonymous, "server-mfa-270");
        draft = change switch
        {
            "resource" => draft with { Resource = new AuditResourceRef(AuditResourceTypes.Account, TargetId.ToString()) },
            "course" => draft with { CourseId = TargetId },
            "details" => draft with { Details = new Dictionary<string, string> { ["reasonCode"] = "attempt_limit" } },
            "success" => draft with { Outcome = AuditOutcome.Success },
            _ => draft
        };
        if (change == "user") context = AdminContext;

        // Act
        var rejection = new AuditDraftValidator().Validate(draft, context);

        // Assert
        Assert.That(rejection, Is.EqualTo(expected));
    }
}
