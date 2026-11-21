using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Platform.Api.Modules.Access.Integration;

// Proposed ATT boundary v1. Its provider implementation/acceptance is not present on main.
public sealed record AuthorizedAnswerRead(AttemptSnapshot Snapshot);
public sealed record AuthorizedDraftWrite(AttemptSnapshot Snapshot, int ExpectedVersion, string Answer,
    Guid ActorId, string CorrelationId, Guid OperationId);
public sealed record DraftWriteReceipt(AttemptView Resource, Guid AuditEventId);

/// <summary>Trusted fixed-origin server-to-server adapter, with no cookies or redirects.</summary>
public sealed class HttpAttemptStore(HttpClient client) : IAttemptStore
{
    public const string Contract = "acc04-att-v1";
    public const string ContractHeader = "X-Attempt-Contract";

    public async ValueTask<PortResult<AttemptSnapshot>> FindAttemptAsync(Guid id, CancellationToken ct = default)
    {
        var r = await SendAsync<AttemptSnapshot>(HttpMethod.Get, $"attempts/{id:D}/metadata", null, ct);
        if (r.Value is { } a && (a.Id != id || a.OwnerId == Guid.Empty || a.CourseId == Guid.Empty || a.Version < 1 || !Enum.IsDefined(a.State)))
            return PortResult<AttemptSnapshot>.Offline();
        return r.Status == 200 && r.Value is not null ? PortResult<AttemptSnapshot>.Found(r.Value)
            : r.Status == 404 ? PortResult<AttemptSnapshot>.Missing() : PortResult<AttemptSnapshot>.Offline();
    }
    public async ValueTask<PortResult<AttemptView>> ReadAnswerAsync(AttemptSnapshot snapshot, CancellationToken ct = default)
    {
        var r = await SendAsync<AttemptView>(HttpMethod.Post, $"attempts/{snapshot.Id:D}/authorized-read", new AuthorizedAnswerRead(snapshot), ct);
        return r is { Status: 200, Value: not null } && r.Value.Id == snapshot.Id && r.Value.Version == snapshot.Version &&
            r.Value.Answer is not null && r.Value.Answer.Length <= AttemptService.MaxAnswerLength
            ? PortResult<AttemptView>.Found(r.Value) : PortResult<AttemptView>.Missing();
    }
    public async ValueTask<WriteResult> UpdateDraftAuthorizedAsync(AttemptSnapshot snapshot, int expectedVersion, string answer,
        Guid actor, string correlation, CancellationToken ct = default)
    {
        if (actor != snapshot.OwnerId || snapshot.State != AttemptState.InProgress || expectedVersion != snapshot.Version)
            return new(WriteStatus.Denied);
        // No automatic retry of a mutation. The provider must atomically bind ALL snapshot fields,
        // persist the answer and its audit receipt, and deduplicate OperationId if transport is retried.
        var r = await SendAsync<DraftWriteReceipt>(HttpMethod.Post, $"attempts/{snapshot.Id:D}/authorized-draft",
            new AuthorizedDraftWrite(snapshot, expectedVersion, answer, actor, correlation, Guid.NewGuid()), ct);
        return r is { Status: 200, Value: { Resource: not null } } && r.Value.Resource.Id == snapshot.Id &&
            r.Value.Resource.Version == expectedVersion + 1 && r.Value.Resource.Answer == answer && r.Value.AuditEventId != Guid.Empty
            ? new(WriteStatus.Updated, r.Value.Resource, r.Value.AuditEventId)
            : new(r.Status == 409 ? WriteStatus.Conflict : WriteStatus.Unavailable);
    }
    private async Task<(int Status, T? Value)> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct) where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var io = timeout.Token;
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add(ContractHeader, Contract);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, io);
            if (!response.IsSuccessStatusCode) return ((int)response.StatusCode, null);
            if (response.StatusCode != HttpStatusCode.OK || !response.Headers.TryGetValues(ContractHeader, out var version) ||
                version.SingleOrDefault() != Contract || response.Content.Headers.ContentLength > 65536) return (503, null);
            await using var stream = await response.Content.ReadAsStreamAsync(io);
            await using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            while (true)
            {
                var n = await stream.ReadAsync(chunk, io);
                if (n == 0) break;
                if (buffer.Length + n > 65536) return (503, null);
                await buffer.WriteAsync(chunk.AsMemory(0,n), io);
            }
            return (200, JsonSerializer.Deserialize<T>(buffer.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or OperationCanceledException or InvalidOperationException)
        { return (503, null); }
    }
}
