using Platform.Api.Modules.Access;
using static Platform.Coursework.Demo.LiveScenario;

namespace Platform.Coursework.Demo;

internal static class LiveAttemptScenario
{
    public static async Task RunAsync(DemoFixture fixture, LiveApiProcess api, AttemptDemoStub stub,
        LiveReport report, CancellationToken ct)
    {
        var draft = $"/api/resource-attempts/{stub.DraftId:D}";
        var submitted = $"/api/resource-attempts/{stub.SubmittedId:D}";
        using var anonymous = new LiveSession(api, report);
        await anonymous.SendAsync("Anonymous draft read is denied", HttpMethod.Get, draft, null, [401], ct);

        using var owner = new LiveSession(api, report);
        using var other = new LiveSession(api, report);
        await owner.LoginAsync("Owner signs in through Identity", fixture.RootA, fixture.Password, ct);
        await other.LoginAsync("Other student on the same demo course signs in", fixture.Delegate, fixture.Password, ct);

        var initial = (await owner.SendAsync("Owner reads their draft", HttpMethod.Get, draft, null, [200], ct)).Parse<AttemptView>();
        report.Check("Initial draft matches the seeded attempt", initial.Id == stub.DraftId && initial.Version == 1);
        const string revisedAnswer = "Edited during the ACC-04 demo";
        var updated = (await owner.SendAsync("Owner saves own draft with CSRF token", HttpMethod.Put, draft + "/answer",
            new UpdateDraftRequest(revisedAnswer, initial.Version), [200], ct)).Parse<AttemptView>();
        report.Check("Saving advances the version and returns the edited answer",
            updated == new AttemptView(stub.DraftId, revisedAnswer, initial.Version + 1));
        var reloaded = (await owner.SendAsync("Owner reloads the saved draft", HttpMethod.Get, draft, null, [200], ct)).Parse<AttemptView>();
        report.Check("Reload returns the saved answer and version", reloaded == updated);

        await owner.SendAsync("A stale version cannot overwrite the newer draft", HttpMethod.Put, draft + "/answer",
            new UpdateDraftRequest("Stale demo edit", initial.Version), [409], ct);
        var foreign = await other.SendAsync("A student cannot read another student's draft", HttpMethod.Get, draft, null, [404], ct);
        var missing = await other.SendAsync("A missing attempt returns the same status", HttpMethod.Get,
            $"/api/resource-attempts/{Guid.NewGuid():D}", null, [404], ct);
        report.Check("Foreign and missing attempt responses have the same body", foreign.Body == missing.Body);
        await other.SendAsync("A student cannot save another student's draft", HttpMethod.Put, draft + "/answer",
            new UpdateDraftRequest("Another student's demo edit", updated.Version), [404], ct);
        await owner.SendAsync("A submitted attempt cannot be edited as a draft", HttpMethod.Put, submitted + "/answer",
            new UpdateDraftRequest("Edit after submission", 1), [404], ct);

        var final = (await owner.SendAsync("Owner reads the draft after the denied writes", HttpMethod.Get, draft, null, [200], ct)).Parse<AttemptView>();
        report.Check("Denied writes leave the saved answer and version unchanged", final == updated && stub.WriteCount == 1);
        report.Stage = "complete";
    }
}
