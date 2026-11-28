using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Access.Integration;

namespace Platform.Coursework.Demo;

/// <summary>Loopback-only KT2 ATT double. Synthetic state and receipts live only for one demo run.</summary>
internal sealed class AttemptDemoStub(DemoFixture fixture) : IAsyncDisposable
{
    public Guid DraftId { get; } = Guid.NewGuid();
    public Guid SubmittedId { get; } = Guid.NewGuid();
    public string Token { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public Uri BaseAddress { get; private set; } = null!;
    public string CertificatePin => certificate.GetCertHashString(HashAlgorithmName.SHA256);

    private readonly X509Certificate2 certificate = LoopbackTls.CreateServerCertificate();
    private readonly object gate = new();
    private readonly Dictionary<Guid, (AttemptSnapshot Snapshot, string Answer)> attempts = [];
    private readonly Dictionary<Guid, (AuthorizedDraftWrite Request, DraftWriteReceipt Receipt)> committed = [];
    private WebApplication? server;
    public int WriteCount { get { lock (gate) return committed.Count; } }

    public async Task StartAsync(CancellationToken ct)
    {
        attempts.Add(DraftId, (new(DraftId, fixture.RootA, fixture.CourseA, AttemptState.InProgress, 1), "Initial demo draft"));
        attempts.Add(SubmittedId, (new(SubmittedId, fixture.RootA, fixture.CourseA, AttemptState.Submitted, 1), "Submitted demo answer"));

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [], EnvironmentName = "Production", ContentRootPath = fixture.PrivateDirectory
        });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = 65536;
            kestrel.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(certificate));
        });
        server = builder.Build();
        var credential = Encoding.ASCII.GetBytes("Bearer " + Token);
        server.Use(async (http, next) =>
        {
            var auth = http.Request.Headers.Authorization;
            if (auth.Count != 1 || http.Request.Headers.ContainsKey("Cookie") ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(auth[0]!), credential))
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            var versions = http.Request.Headers[HttpAttemptStore.ContractHeader];
            if (versions.Count != 1 || versions[0] != HttpAttemptStore.Contract)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            http.Response.Headers[HttpAttemptStore.ContractHeader] = HttpAttemptStore.Contract;
            http.Response.Headers.CacheControl = "no-store";
            await next(http);
        });
        var endpoints = server.MapGroup("/v1/attempts/{id:guid}");
        endpoints.MapGet("/metadata", (Guid id) => Metadata(id));
        endpoints.MapPost("/authorized-read", (Guid id, AuthorizedAnswerRead input) => Read(id, input));
        endpoints.MapPost("/authorized-draft", (Guid id, AuthorizedDraftWrite input) => Write(id, input));
        await server.StartAsync(ct);
        var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        BaseAddress = new Uri(new Uri(address), "/v1/");
    }

    private IResult Metadata(Guid id)
    {
        lock (gate)
            return attempts.TryGetValue(id, out var row) ? Results.Ok(row.Snapshot) : Results.NotFound();
    }

    private IResult Read(Guid id, AuthorizedAnswerRead input)
    {
        lock (gate)
            return attempts.TryGetValue(id, out var row) && row.Snapshot == input.Snapshot
                ? Results.Ok(new AttemptView(id, row.Answer, row.Snapshot.Version)) : Results.NotFound();
    }

    private IResult Write(Guid id, AuthorizedDraftWrite input)
    {
        if (input.Snapshot is not { State: AttemptState.InProgress } snapshot ||
            input.ExpectedVersion < 1 || input.ExpectedVersion != snapshot.Version || input.OperationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(input.CorrelationId) || string.IsNullOrWhiteSpace(input.Answer) ||
            input.Answer.Length > AttemptService.MaxAnswerLength)
            return Results.BadRequest();

        lock (gate)
        {
            if (!attempts.TryGetValue(id, out var row) || snapshot.Id != id ||
                row.Snapshot.OwnerId != snapshot.OwnerId || row.Snapshot.CourseId != snapshot.CourseId ||
                row.Snapshot.State != snapshot.State || input.ActorId != row.Snapshot.OwnerId)
                return Results.NotFound();
            if (committed.TryGetValue(input.OperationId, out var previous))
                return previous.Request == input ? Results.Ok(previous.Receipt) : Results.Conflict();
            if (row.Snapshot.Version != snapshot.Version)
                return Results.Conflict();

            var updated = row.Snapshot with { Version = checked(snapshot.Version + 1) };
            var receipt = new DraftWriteReceipt(new(id, input.Answer, updated.Version), Guid.NewGuid());
            attempts[id] = (updated, input.Answer);
            committed.Add(input.OperationId, (input, receipt));
            return Results.Ok(receipt);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (server is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await using (server) await server.StopAsync(timeout.Token);
            }
        }
        finally { certificate.Dispose(); }
    }
}
