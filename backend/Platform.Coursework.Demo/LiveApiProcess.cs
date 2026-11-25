using System.Diagnostics;
using System.Net;
using System.Net.Security;
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
    private readonly string certificateFile;
    private readonly string certificatePassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly X509Certificate2 certificate;
    private Process? process;
    private Task? stdout;
    private Task? stderr;
    private bool disposed;
    public Uri BaseAddress { get; private set; } = null!;

    public LiveApiProcess(string root, string connection, string directory)
    {
        this.root = root;
        this.connection = connection;
        this.directory = directory;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        certificateFile = Path.Combine(directory, "loopback-only.pfx");
        File.WriteAllBytes(certificateFile, certificate.Export(X509ContentType.Pfx, certificatePassword));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(certificateFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (process is not null) throw new InvalidOperationException("Stop this API child before restarting it.");
        var dll = Path.Combine(root, "backend", "Platform.Api", "bin", "Release", "net10.0", "Platform.Api.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Build the API first.");
        var start = ChildProcess.StartInfo(dll, directory);
        foreach (var name in new[] { "Identity", "Audit", "Team21", "CourseworkAdministration" })
            start.Environment["ConnectionStrings__" + name] = connection;
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = certificateFile;
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = certificatePassword;
        start.Environment["CourseworkAdministration__Enabled"] = "true";
        start.Environment["CourseworkAttempts__Enabled"] = "false"; // no synthetic ATT provider is added
        start.Environment["Kt1Demo__Enabled"] = "false";
        start.Environment["RateLimiting__Login__PermitLimit"] = "100";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        start.Environment["Logging__Console__FormatterName"] = "simple";
        start.ArgumentList.Add("--contentRoot");
        start.ArgumentList.Add(directory);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add("https://127.0.0.1:0");
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

    private static async Task ObserveAsync(StreamReader stream, TaskCompletionSource<Uri>? listening)
    {
        while (await stream.ReadLineAsync() is { } line)
        {
            if (listening is null) continue; // drain diagnostics without retaining credentials or arbitrary HTTP content
            var match = Regex.Match(line, @"Now listening on: (https://127\.0\.0\.1:[0-9]+)", RegexOptions.CultureInvariant);
            if (match.Success && Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out var uri)) listening.TrySetResult(uri);
        }
    }

    public HttpClient CreateClient(bool wrongPin = false, Action<bool>? observeCertificateCheck = null)
    {
        var expected = wrongPin ? RandomNumberGenerator.GetBytes(32) : certificate.GetCertHash(HashAlgorithmName.SHA256);
        var port = BaseAddress.Port;
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
        handler.ServerCertificateCustomValidationCallback = (request, actual, _, errors) =>
        {
            var accepted = request.RequestUri is { Scheme: "https", Host: "127.0.0.1" } uri && uri.Port == port && actual is not null &&
            (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) == 0 &&
            DateTime.UtcNow >= actual.NotBefore.ToUniversalTime() && DateTime.UtcNow <= actual.NotAfter.ToUniversalTime() &&
            CryptographicOperations.FixedTimeEquals(actual.GetCertHash(HashAlgorithmName.SHA256), expected);
            observeCertificateCheck?.Invoke(accepted);
            return accepted;
        };
        return new HttpClient(handler) { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(15) };
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
