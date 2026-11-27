using System.Net;
using System.Net.Http.Json;
using System.Text;
using NUnit.Framework;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Access.Integration;

namespace Platform.Coursework.Tests;

[TestFixture]
public sealed class AttemptProviderAdapterTests
{
    private static readonly AttemptSnapshot Snapshot = new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),AttemptState.InProgress,1);
    private static HttpClient Client(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send)
        => new(new Handler(send)) { BaseAddress=new Uri("https://provider.example.invalid/v1/") };
    private static HttpResponseMessage Reply(object body,bool contract=true)
    {
        var response=new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(body)};
        if(contract)response.Headers.Add(HttpAttemptStore.ContractHeader,HttpAttemptStore.Contract);
        return response;
    }
    [TestCase("wrong-id"),TestCase("empty-owner"),TestCase("invalid-state"),TestCase("zero-version"),TestCase("missing-contract")]
    public async Task InvalidProviderMetadataNeverBecomesAnAuthorizationSnapshot(string mode)
    {
        var bad=mode switch
        { "wrong-id"=>Snapshot with{Id=Guid.NewGuid()},"empty-owner"=>Snapshot with{OwnerId=Guid.Empty},
            "invalid-state"=>Snapshot with{State=(AttemptState)99},"zero-version"=>Snapshot with{Version=0},_=>Snapshot };
        using var client=Client((_,_)=>Task.FromResult(Reply(bad,mode!="missing-contract")));
        Assert.That((await new HttpAttemptStore(client).FindAttemptAsync(Snapshot.Id)).Status,Is.EqualTo(PortStatus.Unavailable));
    }
    [Test]
    public async Task MetadataWithAnswerOrOversizedPayloadIsRejected()
    {
        using var client=Client((_,_)=>Task.FromResult(Reply(new { Snapshot.Id,Snapshot.OwnerId,Snapshot.CourseId,Snapshot.State,Snapshot.Version,Answer=new string('x',70000) })));
        Assert.That((await new HttpAttemptStore(client).FindAttemptAsync(Snapshot.Id)).Status,Is.EqualTo(PortStatus.Unavailable));
        using var smaller=Client((_,_)=>Task.FromResult(Reply(new { Snapshot.Id,Snapshot.OwnerId,Snapshot.CourseId,Snapshot.State,Snapshot.Version,Answer="must not be metadata" })));
        Assert.That((await new HttpAttemptStore(smaller).FindAttemptAsync(Snapshot.Id)).Status,Is.EqualTo(PortStatus.Unavailable));
    }
    [TestCase(HttpStatusCode.Unauthorized),TestCase(HttpStatusCode.InternalServerError),TestCase(HttpStatusCode.TemporaryRedirect)]
    public async Task ProviderErrorsAndRedirectAreUnavailableWithoutAutomaticRetry(HttpStatusCode status)
    {
        int calls=0;
        using var client=Client((_,_)=> { calls++;return Task.FromResult(new HttpResponseMessage(status)); });
        Assert.That((await new HttpAttemptStore(client).FindAttemptAsync(Snapshot.Id)).Status,Is.EqualTo(PortStatus.Unavailable));
        Assert.That(calls,Is.EqualTo(1));
    }
    [Test]
    public async Task SuccessfulWriteRequiresMatchingResourceAndDurableAuditReceipt()
    {
        using var client=Client((_,_)=>Task.FromResult(Reply(new DraftWriteReceipt(new(Snapshot.Id,"answer",2),Guid.Empty))));
        Assert.That((await new HttpAttemptStore(client).UpdateDraftAuthorizedAsync(Snapshot,1,"answer",Snapshot.OwnerId,"test")).Status,Is.EqualTo(WriteStatus.Unavailable));
    }
    [Test]
    public async Task HistoricalStateIsRejectedBeforeProviderMutation()
    {
        int calls=0; using var client=Client((_,_)=> { calls++;return Task.FromResult(Reply(new{})); });
        var result=await new HttpAttemptStore(client).UpdateDraftAuthorizedAsync(Snapshot with{State=AttemptState.Submitted},1,"no",Snapshot.OwnerId,"test");
        Assert.That(result.Status,Is.EqualTo(WriteStatus.Denied)); Assert.That(calls,Is.Zero);
    }
    [Test]
    public async Task TimeoutAlsoBoundsTheResponseBodyAfterHeadersArrive()
    {
        var stalled=new StalledStream();
        using var client=Client((_,_)=>
        {
            var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(stalled)};
            response.Headers.Add(HttpAttemptStore.ContractHeader,HttpAttemptStore.Contract);
            return Task.FromResult(response);
        });
        Assert.That((await new HttpAttemptStore(client).FindAttemptAsync(Snapshot.Id)).Status,Is.EqualTo(PortStatus.Unavailable));
        Assert.That(stalled.Cancelled,Is.True);
    }
    [Test]
    public void CancellationPropagatesInsteadOfBecomingAMissingResource()
    {
        using var client=Client((_,ct)=>Task.FromCanceled<HttpResponseMessage>(ct));
        using var cancel=new CancellationTokenSource(); cancel.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async()=>await new HttpAttemptStore(client).FindAttemptAsync(Snapshot.Id,cancel.Token));
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request,ct); }
    private sealed class StalledStream:Stream
    {
        public bool Cancelled { get; private set; }
        public override bool CanRead=>true; public override bool CanWrite=>false; public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position { get=>throw new NotSupportedException();set=>throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        { try{await Task.Delay(Timeout.Infinite,ct);return 0;}catch(OperationCanceledException){Cancelled=true;throw;} }
        public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken ct)=>ReadAsync(buffer.AsMemory(offset,count),ct).AsTask();
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override void Flush()=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
