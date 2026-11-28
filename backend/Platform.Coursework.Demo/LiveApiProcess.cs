using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Platform.Coursework.Demo;

/// <summary>Own child process only; ephemeral loopback TLS is pinned, never globally trusted.</summary>
internal sealed class LiveApiProcess : IAsyncDisposable
{
    private readonly string root;
    private readonly string connection;
    private readonly string directory;
    private readonly DemoFixture? attemptFixture;
    private readonly AttemptDemoStub? stub;
    private readonly string certificateFile;
    private readonly string certificatePassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly X509Certificate2 certificate;
    private readonly string keyRingFolder;
    private readonly string keyRingCertificateFile;
    private readonly string keyRingPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private Process? process;
    private Task? stdout;
    private Task? stderr;
    private bool disposed;
    private readonly ConcurrentQueue<string> attemptDiagnostics = new();
    public IEnumerable<string> AttemptDiagnostics => attemptDiagnostics.TakeLast(12);
    public Uri BaseAddress { get; private set; } = null!;

    public LiveApiProcess(DemoFixture fixture, AttemptDemoStub? stub = null)
    {
        root = fixture.RepositoryRoot;
        connection = fixture.Connection;
        directory = fixture.PrivateDirectory;
        attemptFixture = stub is null ? null : fixture;
        this.stub = stub;
        certificate = LoopbackTls.CreateCertificate();
        certificateFile = Path.Combine(directory, "loopback-only.pfx");
        File.WriteAllBytes(certificateFile, certificate.Export(X509ContentType.Pfx, certificatePassword));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(certificateFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        // US-22: a Production API does not start without a certificate-protected Data Protection key ring. Its own
        // PFX and key folder live in the same private directory, survive the API restart and are deleted with it.
        keyRingFolder = Path.Combine(directory, "dataprotection-keys");
        keyRingCertificateFile = Path.Combine(directory, "keyring-only.pfx");
        using var keyRingKey = RSA.Create(2048);
        using var keyRingCertificate = new CertificateRequest("CN=SecurityLab live demo key ring", keyRingKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        File.WriteAllBytes(keyRingCertificateFile, keyRingCertificate.Export(X509ContentType.Pfx, keyRingPassword));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyRingCertificateFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (process is not null) throw new InvalidOperationException("Stop this API child before restarting it.");
        var dll = Path.Combine(root, "backend", "Platform.Api", "bin", "Release", "net10.0", "Platform.Api.dll");
        if (stub is not null) dll = typeof(LiveAttemptApi).Assembly.Location;
        if (!File.Exists(dll)) throw new FileNotFoundException("Build the API first.");
        var start = ChildProcess.StartInfo(dll, directory);
        foreach (var name in new[] { "Identity", "Audit", "Team21", "CourseworkAdministration" })
            start.Environment["ConnectionStrings__" + name] = connection;
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = certificateFile;
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = certificatePassword;
        start.Environment["Identity__DataProtection__KeysPath"] = keyRingFolder;
        start.Environment["Identity__DataProtection__CertificatePath"] = keyRingCertificateFile;
        start.Environment["Identity__DataProtection__CertificatePassword"] = keyRingPassword;
        start.Environment["CourseworkAdministration__Enabled"] = stub is null ? "true" : "false";
        start.Environment["CourseworkAttempts__Enabled"] = stub is null ? "false" : "true";
        start.Environment["Kt1Demo__Enabled"] = "false";
        start.Environment["RateLimiting__Login__PermitLimit"] = "100";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        start.Environment["Logging__Console__FormatterName"] = "simple";
        if (stub is null)
        {
            start.ArgumentList.Add("--contentRoot");
            start.ArgumentList.Add(directory);
            start.ArgumentList.Add("--urls");
            start.ArgumentList.Add("https://127.0.0.1:0");
        }
        else
        {
            start.ArgumentList.Add(LiveAttemptApi.ChildArgument);
            start.Environment["CourseworkAttempts__ProviderBaseUrl"] = stub.BaseAddress.AbsoluteUri;
            start.Environment["CourseworkAttempts__ProviderToken"] = stub.Token;
            start.Environment["COURSEWORK_DEMO_ATT_PIN"] = stub.CertificatePin;
            var fixture = attemptFixture!;
            start.Environment["COURSEWORK_DEMO_DRAFT"] = stub.DraftId.ToString("D");
            start.Environment["Logging__LogLevel__Platform.Api.Modules.Access.Api.AttemptRequestAudit"] = "Information";
            start.Environment["COURSEWORK_DEMO_COURSE"] = fixture.CourseA.ToString("D");
            start.Environment["COURSEWORK_DEMO_OWNER"] = fixture.RootA.ToString("D");
            start.Environment["COURSEWORK_DEMO_OTHER_STUDENT"] = fixture.Delegate.ToString("D");
        }
        process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the API child.");
        var listening = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        stdout = ObserveAsync(process.StandardOutput, listening);
        stderr = ObserveAsync(process.StandardError, null);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startup.CancelAfter(TimeSpan.FromSeconds(40));
        var exited = process.WaitForExitAsync(startup.Token);
        var ready = await Task.WhenAny(listening.Task, exited).WaitAsync(startup.Token);
        if (ready != listening.Task) throw new InvalidOperationException("The API exited before its HTTPS listener was ready.");
        BaseAddress = await listening.Task;
    }

    private async Task ObserveAsync(StreamReader stream, TaskCompletionSource<Uri>? listening)
    {
        while (await stream.ReadLineAsync() is { } line)
        {
            if (listening is null) continue; // drain diagnostics without retaining credentials or arbitrary HTTP content
            var readyMarker = stub is null ? "Now listening on: " : "ATT_DEMO_READY ";
            var match = Regex.Match(line, readyMarker + @"(https://127\.0\.0\.1:[0-9]+)", RegexOptions.CultureInvariant);
            if (stub is not null)
            {
                var transport = Regex.Match(line, @"^ATT_DEMO_DIAG ([A-Za-z0-9_]{1,80})$", RegexOptions.CultureInvariant);
                var audit = Regex.Match(line, """ACC04_REQUEST_AUDIT:\{.*?"Reason":"([A-Z0-9_]{1,80})" """.TrimEnd(),
                    RegexOptions.CultureInvariant);
                if (transport.Success) attemptDiagnostics.Enqueue(transport.Groups[1].Value);
                if (audit.Success) attemptDiagnostics.Enqueue("access_" + audit.Groups[1].Value);
            }
            if (match.Success && Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out var uri)) listening.TrySetResult(uri);
        }
    }

    public HttpClient CreateClient(bool wrongPin = false, Action<bool>? observeCertificateCheck = null)
    {
        var expected = wrongPin ? RandomNumberGenerator.GetBytes(32) : certificate.GetCertHash(HashAlgorithmName.SHA256);
        return new HttpClient(LoopbackTls.CreateHandler(BaseAddress, expected, observeCertificateCheck))
        { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task StopAsync()
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(stop.Token);
            await Task.WhenAll(stdout ?? Task.CompletedTask, stderr ?? Task.CompletedTask).WaitAsync(stop.Token);
        }
        finally { process.Dispose(); process = null; }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try { await StopAsync(); }
        finally { certificate.Dispose(); }
    }
}
