using System.Data.Common;
using Platform.Api.Services.Identity;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Platform.Identity.Tests.Services;

[TestFixture]
public sealed class IdentityAuditProducerTests
{
    private static readonly Guid ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TargetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string CorrelationId = "corr-s12-1";

    [Test]
    public async Task LoginThrottled_UsesAnonymousActorAndNoAccountOrRequestDetails()
    {
        var writer = new RecordingWriter();
        var result = await CreateProducer(writer).LoginThrottledAsync(CorrelationId);
        var write = writer.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsRecorded, Is.True);
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthLoginThrottled));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(write.Draft.CourseId, Is.Null);
            Assert.That(write.Draft.Resource, Is.Null);
            Assert.That(write.Draft.Details, Is.Null);
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.Anonymous));
            Assert.That(write.Context.CorrelationId, Is.EqualTo(CorrelationId));
        });
    }

    [Test]
    public async Task LoginSuccess_UsesConfirmedUserAndAccountResource()
    {
        var writer = new RecordingWriter();
        var producer = CreateProducer(writer);

        await producer.LoginSucceededAsync(ActorId, CorrelationId);

        var write = writer.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthLogin));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(write.Draft.Resource, Is.EqualTo(
                new AuditResourceRef(AuditResourceTypes.Account, ActorId.ToString())));
            Assert.That(write.Draft.Details, Is.Null);
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
            Assert.That(write.Context.CorrelationId, Is.EqualTo(CorrelationId));
        }
    }

    [Test]
    public async Task InvalidCredentials_UsesAnonymousActorAndStableReasonCode()
    {
        var writer = new RecordingWriter();
        var producer = CreateProducer(writer);

        await producer.LoginFailedAsync("invalid_credentials", CorrelationId);

        var write = writer.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(write.Draft.Resource, Is.Null);
            Assert.That(write.Draft.Details, Is.EquivalentTo(
                new Dictionary<string, string> { ["reasonCode"] = "invalid_credentials" }));
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.Anonymous));
        }
    }

    [Test]
    public async Task InactiveConfirmedAccount_UsesUserActorWithoutTypedLoginName()
    {
        var writer = new RecordingWriter();
        var producer = CreateProducer(writer);

        await producer.LoginFailedAsync(
            "account_blocked",
            CorrelationId,
            ActorId,
            actorConfirmed: true);

        var write = writer.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
            Assert.That(write.Draft.Resource!.Id, Is.EqualTo(ActorId.ToString()));
            Assert.That(Serialized(write), Does.Not.Contain("email").And.Not.Contain("password"));
        }
    }

    [Test]
    public async Task LockedAccount_UsesAnonymousActorAndConfirmedAccountTarget()
    {
        var writer = new RecordingWriter();
        var producer = CreateProducer(writer);

        await producer.LoginFailedAsync("account_locked", CorrelationId, ActorId);

        var write = writer.Single();
        Assert.Multiple(() =>
        {
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.Anonymous));
            Assert.That(write.Draft.Resource, Is.EqualTo(
                new AuditResourceRef(AuditResourceTypes.Account, ActorId.ToString())));
            Assert.That(write.Draft.Details, Is.EquivalentTo(
                new Dictionary<string, string> { ["reasonCode"] = "account_locked" }));
        });
    }

    [Test]
    public async Task Logout_ContainsNoSessionIdentifier()
    {
        var writer = new RecordingWriter();
        var producer = CreateProducer(writer);

        await producer.LogoutSucceededAsync(ActorId, CorrelationId);

        var write = writer.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthLogout));
            Assert.That(write.Draft.Resource, Is.Null);
            Assert.That(write.Draft.Details, Is.Null);
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
        }
    }

    [Test]
    public async Task SuccessfulStatusChange_UsesCatalogNameAndStructuredStatuses()
    {
        var writer = new RecordingWriter();
        var producer = CreateProducer(writer);
        var eventId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await producer.AccountStatusChangedAsync(
            ActorId,
            TargetId,
            AccountStatus.Active,
            AccountStatus.Blocked,
            CorrelationId,
            eventId);

        var write = writer.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AccountStatusChanged));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(write.Draft.Resource!.Id, Is.EqualTo(TargetId.ToString()));
            Assert.That(write.Draft.Details, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["oldStatus"] = "active",
                ["newStatus"] = "blocked"
            }));
            Assert.That(write.Draft.EventId, Is.EqualTo(eventId));
        }
    }

    [TestCase(AuditOutcome.Denied, "forbidden")]
    [TestCase(AuditOutcome.Failure, "invalid_transition")]
    public async Task RejectedStatusChange_UsesReasonCodeOnly(
        AuditOutcome outcome,
        string reasonCode)
    {
        var writer = new RecordingWriter();
        var producer = CreateProducer(writer);

        await producer.AccountStatusRejectedAsync(
            TargetId, outcome, reasonCode, CorrelationId, ActorId);

        var write = writer.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Draft.Outcome, Is.EqualTo(outcome));
            Assert.That(write.Draft.Details, Is.EquivalentTo(
                new Dictionary<string, string> { ["reasonCode"] = reasonCode }));
            Assert.That(Serialized(write), Does.Not.Contain("free text"));
        }
    }

    [Test]
    public async Task MfaEnrollmentStarted_UsesSuccessAccountResourceAndUserActor()
    {
        var writer = new RecordingWriter();
        await CreateProducer(writer).MfaEnrollmentStartedAsync(ActorId, CorrelationId);
        var write = writer.Single();
        Assert.Multiple(() =>
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaEnrollmentStarted));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(write.Draft.Resource, Is.EqualTo(new AuditResourceRef(AuditResourceTypes.Account, ActorId.ToString())));
            Assert.That(write.Draft.Details, Is.Null);
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
            Assert.That(write.Context.CorrelationId, Is.EqualTo(CorrelationId));
        });
    }

    [Test]
    public async Task MfaEnableFailedWithAlreadyEnabled_UsesFailureReasonAndAccountResource()
    {
        var writer = new RecordingWriter();
        await CreateProducer(writer).MfaEnableFailedAsync(ActorId, "already_enabled", CorrelationId);
        var write = writer.Single();
        Assert.Multiple(() =>
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaEnabled));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(write.Draft.Resource!.Id, Is.EqualTo(ActorId.ToString()));
            Assert.That(write.Draft.Details, Is.EquivalentTo(new Dictionary<string, string> { ["reasonCode"] = "already_enabled" }));
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
            Assert.That(write.Context.CorrelationId, Is.EqualTo(CorrelationId));
        });
    }

    [Test]
    public async Task MfaEnableFailed_UsesFailureReasonAndAccountResource()
    {
        var writer = new RecordingWriter();
        await CreateProducer(writer).MfaEnableFailedAsync(ActorId, "invalid_code", CorrelationId);
        var write = writer.Single();
        Assert.Multiple(() =>
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaEnabled));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(write.Draft.Resource!.Id, Is.EqualTo(ActorId.ToString()));
            Assert.That(write.Draft.Details, Is.EquivalentTo(new Dictionary<string, string> { ["reasonCode"] = "invalid_code" }));
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
            Assert.That(write.Context.CorrelationId, Is.EqualTo(CorrelationId));
        });
    }

    [Test]
    public async Task MfaVerifyFailed_UsesFailureReasonAndAccountResource()
    {
        var writer = new RecordingWriter();
        await CreateProducer(writer).MfaVerifyFailedAsync(ActorId, "replayed_code", CorrelationId);
        var write = writer.Single();
        Assert.Multiple(() =>
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaVerified));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(write.Draft.Resource!.Id, Is.EqualTo(ActorId.ToString()));
            Assert.That(write.Draft.Details, Is.EquivalentTo(new Dictionary<string, string> { ["reasonCode"] = "replayed_code" }));
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
            Assert.That(write.Context.CorrelationId, Is.EqualTo(CorrelationId));
        });
    }

    [Test]
    public async Task MfaVerifyDeniedByAttemptLimit_UsesDeniedReasonAndAccountResource()
    {
        var writer = new RecordingWriter();
        await CreateProducer(writer).MfaVerifyDeniedByAttemptLimitAsync(ActorId, CorrelationId);
        var write = writer.Single();
        Assert.Multiple(() =>
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaVerified));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(write.Draft.Resource!.Id, Is.EqualTo(ActorId.ToString()));
            Assert.That(write.Draft.Details, Is.EquivalentTo(
                new Dictionary<string, string> { ["reasonCode"] = "attempt_limit" }));
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
            Assert.That(write.Context.CorrelationId, Is.EqualTo(CorrelationId));
        });
    }

    [Test]
    public async Task SessionRevoked_UsesSuccessCauseHandleAccountResourceAndUserActor()
    {
        var writer = new RecordingWriter();
        var revokedHandle = Guid.Parse("33333333-3333-3333-3333-333333333333");
        await CreateProducer(writer).SessionRevokedAsync(ActorId, revokedHandle, "user_request", CorrelationId);
        var write = writer.Single();
        Assert.Multiple(() =>
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthSessionRevoked));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(write.Draft.Resource!.Id, Is.EqualTo(ActorId.ToString()));
            Assert.That(write.Draft.Details, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["revocationCause"] = "user_request",
                ["revokedHandle"] = revokedHandle.ToString("D")
            }));
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
            Assert.That(write.Context.CorrelationId, Is.EqualTo(CorrelationId));
        });
    }

    [Test]
    public async Task SessionRevokeFailed_UsesFailureReasonAndAccountResource()
    {
        var writer = new RecordingWriter();
        await CreateProducer(writer).SessionRevokeFailedAsync(ActorId, "not_found", CorrelationId);
        var write = writer.Single();
        Assert.Multiple(() =>
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthSessionRevoked));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(write.Draft.Resource!.Id, Is.EqualTo(ActorId.ToString()));
            Assert.That(write.Draft.Details, Is.EquivalentTo(new Dictionary<string, string> { ["reasonCode"] = "not_found" }));
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
            Assert.That(write.Context.CorrelationId, Is.EqualTo(CorrelationId));
        });
    }

    [Test]
    public async Task ProducerMethods_DoNotAcceptOrWriteCredentialMaterial()
    {
        var writer = new RecordingWriter();
        var producer = CreateProducer(writer);
        using var transaction = new MarkerTransaction();
        await producer.MfaEnrollmentStartedAsync(ActorId, CorrelationId);
        await producer.MfaEnabledAsync(ActorId, CorrelationId, transaction);
        await producer.MfaEnableFailedAsync(ActorId, "invalid_code", CorrelationId);
        await producer.MfaVerifiedAsync(ActorId, CorrelationId, transaction);
        await producer.MfaVerifyFailedAsync(ActorId, "replayed_code", CorrelationId);
        await producer.MfaVerifyDeniedByAttemptLimitAsync(ActorId, CorrelationId);
        await producer.SessionRevokedAsync(
            ActorId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "user_request",
            CorrelationId);
        await producer.SessionRevokeFailedAsync(ActorId, "not_found", CorrelationId);

        var forbiddenParameterNames = new[]
        {
            "code", "secret", "otpauth", "otpauthuri", "password", "token"
        };
        var producerMethods = typeof(IdentityAuditProducer).GetMethods(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.DeclaredOnly);
        var parameterNames = producerMethods
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.Name);

        Assert.Multiple(() =>
        {
            Assert.That(parameterNames.Any(name =>
                forbiddenParameterNames.Contains(name, StringComparer.OrdinalIgnoreCase)), Is.False);
            Assert.That(writer.Writes, Has.Count.EqualTo(8));
            foreach (var write in writer.Writes)
            {
                var detailKeys = write.Draft.Details?.Keys ?? Array.Empty<string>();
                Assert.That(detailKeys.Any(key =>
                    forbiddenParameterNames.Contains(key, StringComparer.OrdinalIgnoreCase)), Is.False);
                Assert.That(Serialized(write), Does.Not.Contain("otpauth").And
                    .Not.Contain("secret").And
                    .Not.Contain("token").And
                    .Not.Contain("password"));
            }
        });
    }

    [TestCase("MfaEnrollmentStarted")]
    [TestCase("MfaEnabled")]
    [TestCase("MfaEnableFailed")]
    [TestCase("MfaVerified")]
    [TestCase("MfaVerifyFailed")]
    [TestCase("MfaVerifyDeniedByAttemptLimit")]
    [TestCase("SessionRevoked")]
    [TestCase("SessionRevokeFailed")]
    public async Task NewProducerMethod_DraftMatchesRealAuditSchema(string method)
    {
        var writer = new RecordingWriter();
        var producer = CreateProducer(writer);
        var revokedHandle = Guid.Parse("33333333-3333-3333-3333-333333333333");
        using var transaction = new MarkerTransaction();

        switch (method)
        {
            case "MfaEnrollmentStarted":
                await producer.MfaEnrollmentStartedAsync(ActorId, CorrelationId);
                break;
            case "MfaEnabled":
                await producer.MfaEnabledAsync(ActorId, CorrelationId, transaction);
                break;
            case "MfaEnableFailed":
                await producer.MfaEnableFailedAsync(ActorId, "already_enabled", CorrelationId);
                break;
            case "MfaVerified":
                await producer.MfaVerifiedAsync(ActorId, CorrelationId, transaction);
                break;
            case "MfaVerifyFailed":
                await producer.MfaVerifyFailedAsync(ActorId, "replayed_code", CorrelationId);
                break;
            case "MfaVerifyDeniedByAttemptLimit":
                await producer.MfaVerifyDeniedByAttemptLimitAsync(ActorId, CorrelationId);
                break;
            case "SessionRevoked":
                await producer.SessionRevokedAsync(ActorId, revokedHandle, "user_request", CorrelationId);
                break;
            case "SessionRevokeFailed":
                await producer.SessionRevokeFailedAsync(ActorId, "not_found", CorrelationId);
                break;
        }

        var write = writer.Single();
        Assert.That(
            new AuditDraftValidator().Validate(write.Draft, write.Context),
            Is.EqualTo(AuditRejectionCode.None));
    }

    [Test]
    public async Task Producer_ReturnsWriterFailureWithoutConvertingItToSuccess()
    {
        var expected = AuditWriteResult.Unavailable();
        var writer = new RecordingWriter(expected);
        var producer = CreateProducer(writer);

        var result = await producer.LoginSucceededAsync(ActorId, CorrelationId);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task Producer_WhenWriterThrows_ReturnsUnavailableWithoutLeakingException()
    {
        var producer = CreateProducer(new ThrowingWriter());

        var result = await producer.LoginSucceededAsync(ActorId, CorrelationId);

        Assert.That(result.Status, Is.EqualTo(AuditWriteStatus.Unavailable));
    }

    [Test]
    public async Task MfaEnabled_IsWrittenInTheCallerTransaction_ForTheUserAndTheirAccount()
    {
        var writer = new RecordingWriter();
        using var transaction = new MarkerTransaction();

        var result = await CreateProducer(writer).MfaEnabledAsync(ActorId, CorrelationId, transaction);

        var write = writer.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsRecorded, Is.True);
            Assert.That(write.Transaction, Is.SameAs(transaction));
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaEnabled));
            Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(write.Draft.Resource, Is.EqualTo(
                new AuditResourceRef(AuditResourceTypes.Account, ActorId.ToString())));
            Assert.That(write.Draft.Details, Is.Null);
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
            Assert.That(write.Context.CorrelationId, Is.EqualTo(CorrelationId));
        }
    }

    [Test]
    public async Task MfaVerified_IsWrittenInTheCallerTransaction()
    {
        var writer = new RecordingWriter();
        using var transaction = new MarkerTransaction();

        await CreateProducer(writer).MfaVerifiedAsync(ActorId, CorrelationId, transaction);

        var write = writer.Single();
        Assert.That(write.Transaction, Is.SameAs(transaction));
        Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaVerified));
        Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
    }

    [TestCase("enrollment.started")]
    [TestCase("enabled.failed")]
    [TestCase("verify.failed")]
    [TestCase("verify.denied")]
    public async Task MfaEventsOutsideATransaction_UseTheTrustedWriter_AndOnlyAReasonCode(string which)
    {
        var writer = new RecordingWriter();
        var producer = CreateProducer(writer);

        _ = which switch
        {
            "enrollment.started" => await producer.MfaEnrollmentStartedAsync(ActorId, CorrelationId),
            "enabled.failed" => await producer.MfaEnableFailedAsync(ActorId, "invalid_code", CorrelationId),
            "verify.failed" => await producer.MfaVerifyFailedAsync(ActorId, "replayed_code", CorrelationId),
            _ => await producer.MfaVerifyDeniedByAttemptLimitAsync(ActorId, CorrelationId)
        };

        var write = writer.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Transaction, Is.Null);
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(ActorId)));
            Assert.That(write.Draft.Resource!.Id, Is.EqualTo(ActorId.ToString()));
            Assert.That(write.Draft.Details?.Keys ?? [], Is.SubsetOf(new[] { "reasonCode" }));
        }
    }

    // Every reason MfaService writes is one that CONTRACTS.md lists for its event and outcome, and the real
    // validator accepts it; otherwise the real writer rejects the refusal record.
    [TestCase("enabled", "invalid_code")]
    [TestCase("enabled", "no_valid_enrollment")]
    [TestCase("enabled", "already_enabled")]
    [TestCase("enabled", "invalid_enrollment")]
    [TestCase("enabled", "session_inactive")]
    [TestCase("verified", "invalid_code")]
    [TestCase("verified", "replayed_code")]
    [TestCase("verified", "invalid_secret")]
    [TestCase("verified", "session_inactive")]
    [TestCase("verified.denied", "attempt_limit")]
    public async Task MfaRefusals_WithEveryReasonTheServiceWrites_PassTheAuditContract(string which, string reason)
    {
        // Arrange
        var writer = new RecordingWriter();
        var producer = CreateProducer(writer);

        // Act
        _ = which switch
        {
            "enabled" => await producer.MfaEnableFailedAsync(ActorId, reason, CorrelationId),
            "verified" => await producer.MfaVerifyFailedAsync(ActorId, reason, CorrelationId),
            _ => await producer.MfaVerifyDeniedByAttemptLimitAsync(ActorId, CorrelationId)
        };

        // Assert
        var write = writer.Single();
        Assert.That(write.Draft.Details!["reasonCode"], Is.EqualTo(reason));
        Assert.That(new Platform.Audit.Application.AuditDraftValidator().Validate(write.Draft, write.Context),
            Is.EqualTo(AuditRejectionCode.None));
    }

    [Test]
    public async Task MfaVerified_WhenTheTransactionalWriterThrows_ReturnsUnavailable()
    {
        using var transaction = new MarkerTransaction();

        var result = await CreateProducer(new ThrowingWriter()).MfaVerifiedAsync(ActorId, CorrelationId, transaction);

        Assert.That(result.Status, Is.EqualTo(AuditWriteStatus.Unavailable));
    }

    private static string Serialized(RecordedWrite write) =>
        string.Join('|',
            write.Draft.EventType,
            write.Draft.Resource?.Id,
            write.Draft.Details is null
                ? null
                : string.Join(',', write.Draft.Details.Select(pair => $"{pair.Key}={pair.Value}")),
            write.Context.Actor.Id,
            write.Context.CorrelationId);

    private static IdentityAuditProducer CreateProducer(ITrustedAuditWriter writer) =>
        new(writer, writer as ITransactionalAuditWriter ?? new ThrowingWriter(), NullLogger<IdentityAuditProducer>.Instance);

    private sealed record RecordedWrite(AuditEventDraft Draft, AuditContext Context, DbTransaction? Transaction = null);

    private sealed class RecordingWriter(
        AuditWriteResult? result = null) : ITrustedAuditWriter, ITransactionalAuditWriter
    {
        private readonly AuditWriteResult _result =
            result ?? AuditWriteResult.Recorded(Guid.Parse("44444444-4444-4444-4444-444444444444"));

        public List<RecordedWrite> Writes { get; } = [];

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            CancellationToken cancellationToken = default)
        {
            Writes.Add(new RecordedWrite(draft, context));
            return ValueTask.FromResult(_result);
        }

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            DbTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            Writes.Add(new RecordedWrite(draft, context, transaction));
            return ValueTask.FromResult(_result);
        }

        public RecordedWrite Single() => Writes.Single();
    }

    private sealed class ThrowingWriter : ITrustedAuditWriter, ITransactionalAuditWriter
    {
        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("sensitive storage diagnostic");

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            DbTransaction transaction,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("sensitive storage diagnostic");
    }

    private sealed class MarkerTransaction : DbTransaction
    {
        public override System.Data.IsolationLevel IsolationLevel => System.Data.IsolationLevel.ReadCommitted;
        protected override DbConnection? DbConnection => null;
        public override void Commit() { }
        public override void Rollback() { }
    }

}
