using System.Text.Json;

namespace Platform.Coursework.Demo;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Run from the repository root using scripts/demo-coursework-live.ps1. No credentials belong in arguments.");
            return 2;
        }
        var root = Path.GetFullPath(Directory.GetCurrentDirectory());
        if (!File.Exists(Path.Combine(root, "backend", "Platform.Api", "Platform.Api.csproj")))
        { Console.Error.WriteLine("The repository root is required."); return 2; }
        var bootstrap = Environment.GetEnvironmentVariable("COURSEWORK_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(bootstrap))
        { Console.Error.WriteLine("Use the isolated launcher or an explicitly dedicated COURSEWORK_TEST_POSTGRES server."); return 2; }

        var report = new LiveReport();
        var results = Path.Combine(root, "TestResults", "coursework-live-" + report.RunId);
        Directory.CreateDirectory(results);
        var evidence = Path.Combine(results, "live-administration.json");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var interrupt = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; interrupt.Cancel(); };
        Console.CancelKeyPress += cancel;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, interrupt.Token);
        DemoFixture? fixture = null;
        LiveApiProcess? api = null;
        try
        {
            report.Stage = "prepare-isolated-provider-state";
            fixture = new DemoFixture(root, bootstrap, report.RunId);
            await fixture.InitializeAsync(linked.Token, stage => report.Stage = stage);
            report.Stage = "start-production-https-kestrel";
            api = new LiveApiProcess(root, fixture.Connection, fixture.PrivateDirectory);
            await api.StartAsync(linked.Token);
            await LiveScenario.RunAsync(fixture, api, report, linked.Token);
            report.Succeeded = true;
        }
        catch (Exception ex)
        {
            // Never persist exception messages, HTTP bodies, connection strings or tokens.
            report.Succeeded = false;
            report.FailureType = ex.GetType().Name;
            Console.Error.WriteLine($"Live demonstration failed at {report.Stage} ({report.FailureType}).");
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            try { if (api is not null) await api.DisposeAsync(); }
            catch (Exception ex) { report.CleanupErrors.Add("API:" + ex.GetType().Name); }
            try
            {
                if (fixture is not null) { await fixture.DisposeAsync(); report.IsolatedStateRemoved = true; }
            }
            catch (Exception ex) { report.CleanupErrors.Add("Fixture:" + ex.GetType().Name); }
            report.FinishedAt = DateTimeOffset.UtcNow;
            if (report.CleanupErrors.Count != 0) report.Succeeded = false;
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(report,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        var passed = report.Checks.Count(c => c.Passed);
        Console.WriteLine($"LIVE ADM08 {(report.Succeeded ? "PASS" : "FAIL")}: {passed}/{report.Checks.Count} checks; HTTP={report.Checks.Count(c => c.Kind == "http")}. Evidence: {evidence}");
        return report.Succeeded ? 0 : 1;
    }
}

internal sealed class LiveReport
{
    public int SchemaVersion { get; } = 1;
    public string RunId { get; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string Mode { get; } = "Real HTTPS/Kestrel, Production host, disposable PostgreSQL providers";
    public string[] Providers { get; } = ["Identity/session", "ACC-01 catalog", "Platform.Access", "ADM-08 PostgreSQL", "central Audit"];
    public string[] UnclosedBoundaries { get; } = ["Actual ATT read/CAS provider", "Actual ACC-02 persistence on main", "Unmapped administrative audit types", "Human contract acceptance"];
    public string Stage { get; set; } = "initializing";
    public bool Succeeded { get; set; }
    public bool IsolatedStateRemoved { get; set; }
    public bool OriginalDelegateCookieReused { get; set; }
    public int ApiRestarts { get; set; }
    public string? FailureType { get; set; }
    public List<string> CleanupErrors { get; } = [];
    public List<LiveCheck> Checks { get; } = [];

    public void Check(string name, bool passed, string kind = "state", int[]? expected = null, int? actual = null)
    {
        Stage = name;
        Checks.Add(new(name, kind, expected, actual, passed));
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}" + (actual is null ? "" : $" : HTTP {actual}"));
        if (!passed) throw new InvalidOperationException("A live check failed.");
    }
}
internal sealed record LiveCheck(string Name, string Kind, int[]? ExpectedStatus, int? ActualStatus, bool Passed);
