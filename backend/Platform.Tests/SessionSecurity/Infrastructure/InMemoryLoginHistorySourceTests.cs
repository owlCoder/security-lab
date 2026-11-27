using Platform.SessionSecurity.Domain;
using Platform.SessionSecurity.Infrastructure;
using Platform.Tests.SessionSecurity.TestDoubles;

// IDN-22 TIM 09
namespace Platform.Tests.SessionSecurity.Infrastructure;

[TestFixture]
public sealed class InMemoryLoginHistorySourceTests
{
    private static readonly DateTimeOffset Now = SyntheticLoginEvents.Now;

    [Test]
    public async Task FindAsync_WhenBothUsersHaveEvents_ReturnsOnlyRequestedSubject()
    {
        var source = new InMemoryLoginHistorySource(SyntheticLoginEvents.ForBothUsers());

        var events = await source.FindAsync(
            SyntheticLoginEvents.UserA, Now.AddDays(-365), Now, outcome: null, skip: 0, take: 50);

        Assert.That(events.Select(loginEvent => loginEvent.SubjectId), Is.All.EqualTo(SyntheticLoginEvents.UserA));
    }

    [Test]
    public async Task FindAsync_WhenSubjectIsUnknown_ReturnsEmptyList()
    {
        var source = new InMemoryLoginHistorySource(SyntheticLoginEvents.ForBothUsers());

        var events = await source.FindAsync("user-c", Now.AddDays(-365), Now, outcome: null, skip: 0, take: 50);

        Assert.That(events, Is.Empty);
    }

    [Test]
    public async Task FindAsync_WhenEventIsExactlyOnRangeBounds_IncludesIt()
    {
        var oldest = Now.AddDays(-10);
        var source = new InMemoryLoginHistorySource(SyntheticLoginEvents.ForUserA());

        var events = await source.FindAsync(
            SyntheticLoginEvents.UserA, oldest, Now.AddHours(-1), outcome: null, skip: 0, take: 50);

        Assert.That(
            events.Select(loginEvent => loginEvent.OccurredAt),
            Is.EqualTo(new[] { Now.AddHours(-1), Now.AddDays(-2), oldest }));
    }

    [TestCase(LoginOutcomeFilter.Succeeded, true)]
    [TestCase(LoginOutcomeFilter.Failed, false)]
    public async Task FindAsync_WhenOutcomeIsGiven_ReturnsOnlyMatchingOutcome(
        LoginOutcomeFilter outcome, bool expectedSucceeded)
    {
        var source = new InMemoryLoginHistorySource(SyntheticLoginEvents.ForUserA());

        var events = await source.FindAsync(
            SyntheticLoginEvents.UserA, Now.AddDays(-365), Now, outcome, skip: 0, take: 50);

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.Not.Empty);
            Assert.That(events.Select(loginEvent => loginEvent.Succeeded), Is.All.EqualTo(expectedSucceeded));
        });
    }

    [Test]
    public async Task FindAsync_WhenEventsAreUnordered_ReturnsNewestFirst()
    {
        var source = new InMemoryLoginHistorySource(SyntheticLoginEvents.ForUserA().Reverse());

        var events = await source.FindAsync(
            SyntheticLoginEvents.UserA, Now.AddDays(-365), Now, outcome: null, skip: 0, take: 50);

        Assert.That(events.Select(loginEvent => loginEvent.OccurredAt), Is.Ordered.Descending);
    }

    [Test]
    public async Task FindAsync_WhenSkipAndTakeAreGiven_ReturnsOnlyThatPage()
    {
        var source = new InMemoryLoginHistorySource(SyntheticLoginEvents.ForUserA());

        var events = await source.FindAsync(
            SyntheticLoginEvents.UserA, Now.AddDays(-365), Now, outcome: null, skip: 1, take: 2);

        Assert.That(
            events.Select(loginEvent => loginEvent.OccurredAt),
            Is.EqualTo(new[] { Now.AddDays(-2), Now.AddDays(-10) }));
    }

    [Test]
    public void FindAsync_WhenCancelled_Throws()
    {
        var source = new InMemoryLoginHistorySource(SyntheticLoginEvents.ForUserA());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () => await source.FindAsync(
            SyntheticLoginEvents.UserA, Now.AddDays(-1), Now, outcome: null, skip: 0, take: 1, cancellation.Token));
    }
}
