using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Npgsql;
using Platform.Identity.Entities;
using Platform.Identity.Helpers;
using Platform.Identity.Interfaces.Mfa;
using Platform.Identity.Persistence.Mfa;
using Platform.Identity.Tests.Helpers;
using Platform.Identity.Testing;

namespace Platform.Identity.Tests.Integration.Mfa;

/// <summary>
/// IDN-06 with IDN-03 and IDN-07: MfaService reads the session and the account without a lock, then writes in a
/// transaction. A block or a revocation in between must win. The interruption runs right before that transaction
/// begins: a block that is still open when the write starts (the write waits for its lock), or a revocation already
/// committed.
/// </summary>
[NonParallelizable]
public sealed class MfaWriteRaceIntegrationTests : MfaIntegrationTestBase
{
    [Test]
    [Category("US-17")]
    [Category("IDN-03")]
    public async Task EnrollConfirm_AccountBlockedBeforeTheWrite_ReturnsUnauthorized_AndMfaStaysDisabled()
    {
        // Arrange
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-confirm-blocked@platform.com", Password);
        await MfaTestData.AddPendingAsync(Factory, user.Id, secret, DateTimeOffset.UtcNow.AddMinutes(5));
        var session = await SessionTestData.CreateAsync(Factory, user.Id);
        var block = new BlockStillOpenAtTheWrite(user.Id);
        using var interrupted = WithInterruptionBeforeTheWrite(block.StartAsync);
        using var client = interrupted.CreateCsrfClient();

        // Act
        var response = await PostWithSessionAsync(
            "/api/auth/mfa/enroll/confirm", session.RawToken, new { Code = MfaTestData.CurrentCode(secret) }, client);
        await block.CommittedAfterTheWriteWaited;

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await GetCredentialsAsync(user.Id))!.MfaEnabled, Is.False);
        // Only the transactional writer stores rows here (refusals go to the test's trusted writer).
        Assert.That(await AuditTestData.RowsAsync(user.Id, "auth.mfa.enabled"), Is.Empty);
    }

    [Test]
    [Category("US-17")]
    [Category("IDN-07")]
    public async Task EnrollConfirm_SessionRevokedBeforeTheWrite_ReturnsUnauthorized_AndMfaStaysDisabled()
    {
        // Arrange
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-confirm-revoked@platform.com", Password);
        await MfaTestData.AddPendingAsync(Factory, user.Id, secret, DateTimeOffset.UtcNow.AddMinutes(5));
        var session = await SessionTestData.CreateAsync(Factory, user.Id);
        using var interrupted = WithInterruptionBeforeTheWrite(() => SessionTestData.RevokeAsync(session.Session.Id));
        using var client = interrupted.CreateCsrfClient();

        // Act
        var response = await PostWithSessionAsync(
            "/api/auth/mfa/enroll/confirm", session.RawToken, new { Code = MfaTestData.CurrentCode(secret) }, client);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await GetCredentialsAsync(user.Id))!.MfaEnabled, Is.False);
        // Only the transactional writer stores rows here (refusals go to the test's trusted writer).
        Assert.That(await AuditTestData.RowsAsync(user.Id, "auth.mfa.enabled"), Is.Empty);
    }

    [Test]
    [Category("US-17")]
    [Category("IDN-03")]
    public async Task Enroll_AccountBlockedBeforeTheWrite_ReturnsUnauthorized_AndStoresNoEnrollment()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "mfa-enroll-blocked@platform.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, user.Id);
        var block = new BlockStillOpenAtTheWrite(user.Id);
        using var interrupted = WithInterruptionBeforeTheWrite(block.StartAsync);
        using var client = interrupted.CreateCsrfClient();

        // Act
        var response = await PostWithSessionAsync("/api/auth/mfa/enroll", session.RawToken, new { }, client);
        await block.CommittedAfterTheWriteWaited;

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await GetCredentialsAsync(user.Id), Is.Null, "no pending enrollment is stored");
        Assert.That(await AuditTestData.RowsAsync(user.Id, "auth.mfa.enrollment.started"), Is.Empty);
    }

    [Test]
    [Category("US-17")]
    [Category("IDN-07")]
    public async Task Enroll_SessionRevokedBeforeTheWrite_ReturnsUnauthorized_AndStoresNoEnrollment()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "mfa-enroll-revoked@platform.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, user.Id);
        using var interrupted = WithInterruptionBeforeTheWrite(() => SessionTestData.RevokeAsync(session.Session.Id));
        using var client = interrupted.CreateCsrfClient();

        // Act
        var response = await PostWithSessionAsync("/api/auth/mfa/enroll", session.RawToken, new { }, client);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await GetCredentialsAsync(user.Id), Is.Null, "no pending enrollment is stored");
        Assert.That(await AuditTestData.RowsAsync(user.Id, "auth.mfa.enrollment.started"), Is.Empty);
    }

    // The real store, through a mock that runs the interruption once, before the writing transaction begins.
    private static WebApplicationFactory<Program> WithInterruptionBeforeTheWrite(Func<Task> interruption) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMfaCredentialsStore>();
            services.AddScoped<EfMfaCredentialsStore>();
            services.AddScoped<IMfaCredentialsStore>(provider =>
                InterruptedBeforeTheWrite(provider.GetRequiredService<EfMfaCredentialsStore>(), interruption));
        }));

    private static IMfaCredentialsStore InterruptedBeforeTheWrite(IMfaCredentialsStore inner, Func<Task> interruption)
    {
        var store = new Mock<IMfaCredentialsStore>(MockBehavior.Strict);
        store.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid userId, CancellationToken ct) => inner.GetAsync(userId, ct));
        store.Setup(s => s.GetEmailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid userId, CancellationToken ct) => inner.GetEmailAsync(userId, ct));
        store.Setup(s => s.RecordFailedAttemptAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns((Guid userId, int maxAttempts, DateTimeOffset now, DateTimeOffset lockedUntil, CancellationToken ct) =>
                inner.RecordFailedAttemptAsync(userId, maxAttempts, now, lockedUntil, ct));
        store.Setup(s => s.BeginAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await interruption();
                return await inner.BeginAsync(ct);
            });
        return store.Object;
    }

    private static async Task<bool> AnyQueryWaitsForALockAsync()
    {
        await using var connection = new NpgsqlConnection(TestDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock')
            """, connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// The statements of ChangeAccountStatus (IDN-03): lock the account, block it, revoke its sessions. The
    /// transaction stays open when the write starts and commits only once a query waits for its lock, so the
    /// write has to wait for the block and then see it. Without that wait it never commits and the test fails.
    /// </summary>
    private sealed class BlockStillOpenAtTheWrite(Guid userId)
    {
        private Task _committed = Task.FromException(new InvalidOperationException("The write never started."));

        public Task CommittedAfterTheWriteWaited => _committed;

        public async Task StartAsync()
        {
            var connection = new NpgsqlConnection(TestDatabaseFixture.ConnectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            foreach (var sql in new[]
                     {
                         "SELECT 1 FROM identity.accounts WHERE id = @id FOR UPDATE",
                         "UPDATE identity.accounts SET status = 'Blocked' WHERE id = @id",
                         "UPDATE identity.sessions SET revoked_at = now() WHERE user_id = @id AND revoked_at IS NULL"
                     })
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("id", userId);
                await command.ExecuteNonQueryAsync();
            }

            _committed = CommitOnceTheWriteWaitsAsync(connection, transaction);
        }

        private static async Task CommitOnceTheWriteWaitsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            await using (connection)
            await using (transaction)
            {
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!await AnyQueryWaitsForALockAsync())
                {
                    if (DateTime.UtcNow > deadline)
                        throw new TimeoutException("The write did not wait for the block.");
                    await Task.Delay(20);
                }

                await transaction.CommitAsync();
            }
        }
    }
}
