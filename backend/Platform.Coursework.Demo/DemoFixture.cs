using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Api.Modules.Administration.Persistence;
using Platform.Audit.Composition;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Identity.Services;
using Team21.Acc.Core.Courses;
using Team21.Acc.Infrastructure.Courses.Data;

namespace Platform.Coursework.Demo;

/// <summary>Test-tool state only. No seed/provisioning route is added to Platform.Api.</summary>
internal sealed class DemoFixture : IAsyncDisposable
{
    private readonly string root;
    private readonly string bootstrap;
    private readonly string database;
    private bool created;
    private bool disposed;
    public string Connection { get; }
    public string PrivateDirectory { get; }
    public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public Guid RootA { get; } = Guid.NewGuid();
    public Guid RootB { get; } = Guid.NewGuid();
    public Guid Delegate { get; } = Guid.NewGuid();
    public Guid Viewer { get; } = Guid.NewGuid();
    public Guid Teacher { get; } = Guid.NewGuid();
    public Guid CourseA { get; } = Guid.NewGuid();
    public Guid CourseB { get; } = Guid.NewGuid();
    public static string Email(Guid id) => $"{id:N}@live-coursework.example.invalid";

    public DemoFixture(string root, string bootstrap, string runId)
    {
        this.root = root;
        this.bootstrap = bootstrap;
        database = "cw_live_" + runId;
        Connection = new NpgsqlConnectionStringBuilder(bootstrap) { Database = database, Timeout = 5, CommandTimeout = 5 }.ConnectionString;
        PrivateDirectory = Path.Combine(Path.GetTempPath(), "securitylab-live-" + runId);
        Directory.CreateDirectory(PrivateDirectory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(PrivateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public PlatformIdentityDbContext Identity() => new(new DbContextOptionsBuilder<PlatformIdentityDbContext>()
        .UseNpgsql(Connection, p => p.MigrationsHistoryTable("__EFMigrationsHistory", "identity")).Options);
    public CoursesDbContext Catalog() => new(new DbContextOptionsBuilder<CoursesDbContext>()
        .UseNpgsql(Connection, p => p.MigrationsHistoryTable("__EFMigrationsHistory", "courses")).Options);

    public async Task InitializeAsync(CancellationToken ct, Action<string>? stage = null)
    {
        stage?.Invoke("create-own-isolated-database");
        await using (var db = new NpgsqlConnection(bootstrap))
        {
            await db.OpenAsync(ct);
            // database is generated exclusively from our GUID, never a caller-supplied database name.
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", db);
            await create.ExecuteNonQueryAsync(ct);
            created = true;
        }
        stage?.Invoke("migrate-and-seed-fictional-identity-accounts");
        await using (var identity = Identity())
        {
            await identity.Database.MigrateAsync(ct);
            var hash = new BCryptPasswordHasher().Hash(Password);
            foreach (var id in new[] { RootA, RootB, Delegate, Viewer, Teacher })
            {
                var account = new UserAccount(id, Email(id), AccountStatus.Active,
                    id == Teacher ? AccountRole.Teacher : AccountRole.Student);
                account.SetPasswordHash(hash);
                identity.Accounts.Add(account);
            }
            await identity.SaveChangesAsync(ct);
        }
        stage?.Invoke("migrate-real-central-audit-schema");
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(Connection, ct);
        stage?.Invoke("migrate-and-seed-isolated-catalog");
        await using (var courses = Catalog())
        {
            await courses.Database.MigrateAsync(ct);
            courses.Courses.Add(new Course("LIVE-A", "Live catalog course A", "Isolated KT2 fixture", RootA) { Id = CourseA });
            courses.Courses.Add(new Course("LIVE-B", "Live catalog course B", "Isolated KT2 fixture", RootB) { Id = CourseB });
            await courses.SaveChangesAsync(ct);
        }
        // Use the real operator executable, including its normal argument/configuration handling.
        stage?.Invoke("operator-migrate-and-bootstrap-two-roots");
        await OperatorAsync(["migrate"], ct);
        await OperatorAsync(["bootstrap-root", CourseA.ToString("D"), RootA.ToString("D"), "--confirm-initial-root"], ct);
        await OperatorAsync(["bootstrap-root", CourseB.ToString("D"), RootB.ToString("D"), "--confirm-initial-root"], ct);
    }

    public async Task OperatorAsync(string[] arguments, CancellationToken ct)
    {
        var dll = Path.Combine(root, "backend", "Platform.Coursework.AdminTool", "bin", "Release", "net10.0", "Platform.Coursework.AdminTool.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Build the operator tool first.");
        var start = ChildProcess.StartInfo(dll, PrivateDirectory);
        start.Environment["COURSEWORK_MIGRATION_CONNECTION"] = Connection;
        start.Environment["ConnectionStrings__Identity"] = Connection;
        start.Environment["ConnectionStrings__Team21"] = Connection;
        start.Environment["ConnectionStrings__Audit"] = Connection;
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the operator tool.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(output, errors);
            if (process.ExitCode != 0) throw new InvalidOperationException("The operator command failed.");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }

    public async Task<long> CountAsync(string sql, CancellationToken ct)
    {
        await using var db = new NpgsqlConnection(Connection);
        await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, db);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (created)
            {
                NpgsqlConnection.ClearAllPools();
                await using var db = new NpgsqlConnection(bootstrap);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await db.OpenAsync(timeout.Token);
                await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", db);
                await drop.ExecuteNonQueryAsync(timeout.Token);
            }
        }
        finally { if (Directory.Exists(PrivateDirectory)) Directory.Delete(PrivateDirectory, recursive: true); }
    }
}

internal static class ChildProcess
{
    public static ProcessStartInfo StartInfo(string dll, string directory)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        // Inherit OS/runtime essentials only; never inherit real provider credentials/configuration.
        // Unix may contain HTTP_PROXY and http_proxy simultaneously. Copy only named essentials,
        // without collapsing the entire caller environment into a case-insensitive dictionary.
        var inherited = new Dictionary<string, string>();
        foreach (var key in new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "TMPDIR", "HOME", "LANG", "LC_ALL", "DOTNET_ROOT", "DOTNET_ROOT_X64" })
            if (Environment.GetEnvironmentVariable(key) is { } value) inherited[key] = value;
        start.Environment.Clear();
        foreach (var (key, value) in inherited) start.Environment[key] = value;
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.ArgumentList.Add(dll);
        return start;
    }
}
