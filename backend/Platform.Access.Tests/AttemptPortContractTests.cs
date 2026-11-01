using System.Collections.Concurrent;
using NUnit.Framework;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Access.Demo;

namespace Platform.Access.Tests;

public sealed class AttemptPortContractTests
{
    [Test] public void UnknownAttemptHasExplicitNegativeResponse()
    {
        IAttemptReader reader = new DemoData();
        var result = reader.FindAttempt(DemoData.MissingAttempt);
        Assert.That(result.Status, Is.EqualTo(PortStatus.NotFound));
        Assert.That(result.Value, Is.Null);
    }
    [Test] public void ContentCannotBeReadThroughForgedSnapshot()
    {
        var data = new DemoData();
        var snapshot = data.FindAttempt(DemoData.DraftAttempt).Value!;
        Assert.That(data.ReadAnswer(snapshot with { OwnerId = DemoData.Marko }).Status, Is.EqualTo(PortStatus.NotFound));
    }
    [Test] public void CompareAndSwapCommitsExactlyOneCompetingUpdate()
    {
        var data = new DemoData();
        var snapshot = data.FindAttempt(DemoData.DraftAttempt).Value!;
        var outcomes = new ConcurrentBag<DraftWriteStatus>();
        Parallel.Invoke(
            () => outcomes.Add(data.TryUpdateDraft(snapshot, 1, "Prvi konkurent.").Status),
            () => outcomes.Add(data.TryUpdateDraft(snapshot, 1, "Drugi konkurent.").Status));
        var after = data.FindAttempt(DemoData.DraftAttempt).Value!;
        Assert.Multiple(() =>
        {
            Assert.That(outcomes.Count(x => x == DraftWriteStatus.Updated), Is.EqualTo(1));
            Assert.That(outcomes.Count(x => x == DraftWriteStatus.Conflict), Is.EqualTo(1));
            Assert.That(after.Version, Is.EqualTo(2));
            Assert.That(data.ReadAnswer(snapshot).Status, Is.EqualTo(PortStatus.NotFound));
            Assert.That(data.ReadAnswer(after).Value?.Answer, Is.AnyOf("Prvi konkurent.", "Drugi konkurent."));
        });
    }
    [Test] public void SubmittedSnapshotCannotBeWrittenEvenByDirectAdapterCall()
    {
        var data = new DemoData();
        var snapshot = data.FindAttempt(DemoData.AnaAttempt).Value!;
        var old = data.ReadAnswer(snapshot).Value;
        Assert.That(data.TryUpdateDraft(snapshot, snapshot.Version, "Prepisivanje.").Status, Is.EqualTo(DraftWriteStatus.Conflict));
        Assert.That(data.ReadAnswer(snapshot).Value, Is.EqualTo(old));
    }
    [Test] public void NewFixtureRestoresOriginalSyntheticState()
    {
        var changed = new DemoData();
        var initial = changed.FindAttempt(DemoData.DraftAttempt).Value!;
        changed.TryUpdateDraft(initial, 1, "Izmena.");
        var fresh = new DemoData();
        var restored = fresh.FindAttempt(DemoData.DraftAttempt).Value!;
        Assert.That(restored.Version, Is.EqualTo(1));
        Assert.That(fresh.ReadAnswer(restored).Value?.Answer, Is.EqualTo("Nepredati testni odgovor."));
    }
}
