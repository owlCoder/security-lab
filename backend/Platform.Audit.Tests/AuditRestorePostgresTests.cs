using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Domain;
using Platform.Audit.Infrastructure.Persistence;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

[TestFixture]
[Category("AuditPostgres")]
[Category("S17")]
public sealed class AuditRestorePostgresTests
{
    [Test]
    public async Task RestoreThenBootstrap_PreservesEventsTriggerAndEnforcesExplicitRuntimePrivileges()
    {
        var configured = Environment.GetEnvironmentVariable("AUDIT_TEST_ADMIN_CONNECTION_STRING");
        var runtimeConfigured = Environment.GetEnvironmentVariable("AUDIT_TEST_RUNTIME_CONNECTION_STRING");
        var container = Environment.GetEnvironmentVariable("AUDIT_TEST_POSTGRES_CONTAINER");
        var binaries = Environment.GetEnvironmentVariable("AUDIT_TEST_PG_BIN");
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(runtimeConfigured) ||
            (string.IsNullOrWhiteSpace(container) && string.IsNullOrWhiteSpace(binaries)))
        {
            Assert.Ignore("Set the Audit test connections and AUDIT_TEST_POSTGRES_CONTAINER or AUDIT_TEST_PG_BIN.");
            return;
        }

        var admin = new NpgsqlConnectionStringBuilder(configured) { Pooling = false };
        var runtime = new NpgsqlConnectionStringBuilder(runtimeConfigured) { Pooling = false };
        var suffix = Guid.NewGuid().ToString("N");
        var source = "audit_restore_source_" + suffix;
        var target = "audit_restore_target_" + suffix;
        var archive = string.IsNullOrWhiteSpace(container)
            ? Path.Combine(Path.GetTempPath(), $"audit-{suffix}.dump")
            : $"/tmp/audit-{suffix}.dump";
        var created = new List<string>();
        var bootstrap = ReadBootstrapScript();
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync();
        try
        {
            foreach (var database in new[] { source, target })
            {
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", control);
                await create.ExecuteNonQueryAsync();
                created.Add(database);
                await using var restrict = new NpgsqlCommand($"REVOKE CONNECT ON DATABASE \"{database}\" FROM PUBLIC", control);
                await restrict.ExecuteNonQueryAsync();
            }
            admin.Database = source;
            await using (var database = OpenContext(admin.ConnectionString))
            {
                await database.Database.MigrateAsync();
                Assert.That(runtime.Username, Is.EqualTo("securitylab_audit_runtime"));
            }
            await RunTool("psql", ["--no-psqlrc", "--set=ON_ERROR_STOP=1"], admin, container, binaries,
                bootstrap, runtime.Password);

            var expected = ValidPerEventType().Select(example => AuditEvent.Create(Guid.NewGuid(),
                new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero), example.Draft, example.Context)).ToArray();
            await using (var database = OpenContext(admin.ConnectionString))
                foreach (var record in expected)
                    Assert.That(await new PostgresAuditEventStore(database).AppendAsync(record),
                        Is.EqualTo(AuditAppendStatus.Appended));

            await RunTool("pg_dump", ["--format=custom", "--file", archive], admin, container, binaries);
            await RunTool("pg_restore", ["--exit-on-error", "--no-owner", "--dbname", target, archive],
                admin, container, binaries);

            // A database restored without --create has no source database-level CONNECT grant.
            Assert.That(await HasRuntimeConnect(control, target), Is.False);
            admin.Database = target;
            await RunTool("psql", ["--no-psqlrc", "--set=ON_ERROR_STOP=1"], admin, container, binaries,
                bootstrap, runtime.Password);
            Assert.That(await HasRuntimeConnect(control, target), Is.True);
            runtime.Database = target;
            await using (var restored = OpenContext(runtime.ConnectionString))
            {
                Assert.That(await restored.Events.CountAsync(), Is.EqualTo(expected.Length));
                var store = new PostgresAuditEventStore(restored);
                foreach (var record in expected)
                {
                    var actual = await store.FindAsync(record.Id);
                    Assert.That(actual, Is.Not.Null);
                    Assert.That(actual!.HasSameContentAs(record), Is.True);
                    Assert.That(actual.OccurredAt, Is.EqualTo(record.OccurredAt));
                }
                var next = AuditEvent.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, StatusChange(), UserContext);
                Assert.That(await store.AppendAsync(next), Is.EqualTo(AuditAppendStatus.Appended));
            }
            await using var login = new NpgsqlConnection(runtime.ConnectionString);
            await login.OpenAsync();
            foreach (var sql in new[]
            {
                "UPDATE audit.audit_events SET outcome = 'Failure'",
                "DELETE FROM audit.audit_events",
                "TRUNCATE audit.audit_events",
                "ALTER TABLE audit.audit_events DISABLE TRIGGER ALL"
            })
            {
                await using var transaction = await login.BeginTransactionAsync();
                await using var forbidden = new NpgsqlCommand(sql, login, transaction);
                var error = Assert.ThrowsAsync<PostgresException>(async () => await forbidden.ExecuteNonQueryAsync());
                Assert.That(error!.SqlState, Is.EqualTo(PostgresErrorCodes.InsufficientPrivilege));
                await transaction.RollbackAsync();
            }
            await using var trigger = new NpgsqlCommand("""
                SELECT tgenabled = 'A' FROM pg_trigger
                WHERE tgrelid = 'audit.audit_events'::regclass AND tgname = 'audit_events_append_only'
                """, login);
            Assert.That(await trigger.ExecuteScalarAsync(), Is.EqualTo(true));
        }
        finally
        {
            if (string.IsNullOrWhiteSpace(container))
                File.Delete(archive);
            foreach (var database in created)
            {
                await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", control);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }

    private static AuditDbContext OpenContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit")).Options);

    private static async Task<bool> HasRuntimeConnect(NpgsqlConnection connection, string database)
    {
        await using var command = new NpgsqlCommand(
            "SELECT has_database_privilege('securitylab_audit_runtime', @database, 'CONNECT')", connection);
        command.Parameters.AddWithValue("database", database);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static string ReadBootstrapScript()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "scripts", "audit", "bootstrap-runtime.sql");
            if (File.Exists(path))
                return File.ReadAllText(path);
        }
        throw new FileNotFoundException("Run the restore test from a checkout containing scripts/audit/bootstrap-runtime.sql.");
    }

    private static async Task RunTool(string tool, string[] arguments,
        NpgsqlConnectionStringBuilder admin, string? container, string? binaries,
        string? input = null, string? runtimePassword = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(container)
                ? Path.Combine(binaries!, OperatingSystem.IsWindows() ? tool + ".exe" : tool) : "docker",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null
        };
        info.Environment["PGPASSWORD"] = admin.Password;
        info.Environment["PGHOST"] = "127.0.0.1";
        info.Environment["PGPORT"] = string.IsNullOrWhiteSpace(container) ? admin.Port.ToString() : "5432";
        info.Environment["PGUSER"] = admin.Username;
        info.Environment["PGDATABASE"] = admin.Database;
        if (runtimePassword is not null)
            info.Environment["AUDIT_RUNTIME_PASSWORD"] = runtimePassword;
        if (!string.IsNullOrWhiteSpace(container))
        {
            info.ArgumentList.Add("exec");
            if (input is not null)
                info.ArgumentList.Add("--interactive");
            var variables = new List<string> { "PGPASSWORD", "PGHOST", "PGPORT", "PGUSER", "PGDATABASE" };
            if (runtimePassword is not null)
                variables.Add("AUDIT_RUNTIME_PASSWORD");
            foreach (var name in variables)
            {
                info.ArgumentList.Add("--env");
                info.ArgumentList.Add(name);
            }
            info.ArgumentList.Add(container);
            info.ArgumentList.Add(tool);
        }
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("PostgreSQL tool could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
            Assert.That(process.ExitCode, Is.Zero, $"{tool} failed; tool output is omitted to protect connection details.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
