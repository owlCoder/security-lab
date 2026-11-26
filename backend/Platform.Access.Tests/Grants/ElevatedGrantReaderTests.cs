using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Grants;

namespace Platform.Access.Tests.Grants;

[TestFixture]
[Property("RequirementId", "ACC-08-05")]
public sealed class ElevatedGrantReaderTests
{
    private const string Action = "results.sensitive.read";

    private readonly Guid holderId = Guid.NewGuid();

    private ElevatedGrant Grant(Guid holder, string action) =>
        new(
            Guid.NewGuid(),
            holder,
            Guid.NewGuid(),
            action,
            new GrantScope(GrantScopeKind.Course, Guid.NewGuid().ToString("D")),
            "incident review",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddDays(1),
            false);

    [Test]
    public async Task EmptyReader_ReturnsNoGrants()
    {
        IElevatedGrantReader reader = new EmptyElevatedGrantReader();

        var grants = await reader.GetForHolderAsync(holderId, Action, CancellationToken.None);

        Assert.That(grants, Is.Empty);
    }

    [Test]
    public async Task InMemoryReader_ReturnsOnlyGrantsOfHolderAndAction()
    {
        var matching = Grant(holderId, Action);
        IElevatedGrantReader reader = new InMemoryElevatedGrantReader(
        [
            matching,
            Grant(Guid.NewGuid(), Action),
            Grant(holderId, "attempt.read")
        ]);

        var grants = await reader.GetForHolderAsync(holderId, Action, CancellationToken.None);

        Assert.That(grants, Is.EqualTo(new[] { matching }));
    }

    [Test]
    public async Task InMemoryReader_ReturnsNothingForUnknownHolder()
    {
        IElevatedGrantReader reader = new InMemoryElevatedGrantReader([Grant(holderId, Action)]);

        var grants = await reader.GetForHolderAsync(Guid.NewGuid(), Action, CancellationToken.None);

        Assert.That(grants, Is.Empty);
    }

    [Test]
    public async Task InMemoryReader_IgnoresLaterChangesToSourceCollection()
    {
        var source = new List<ElevatedGrant> { Grant(holderId, Action) };
        IElevatedGrantReader reader = new InMemoryElevatedGrantReader(source);

        source.Add(Grant(holderId, Action));
        var grants = await reader.GetForHolderAsync(holderId, Action, CancellationToken.None);

        Assert.That(grants, Has.Count.EqualTo(1));
    }

    [Test]
    public void InMemoryReader_RejectsNullGrantCollection() =>
        Assert.Throws<ArgumentNullException>(() => new InMemoryElevatedGrantReader(null!));
}
