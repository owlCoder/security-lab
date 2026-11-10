using Platform.RevisionBinding.Contracts;
using Platform.RevisionBinding.Domain;

namespace Platform.RevisionBinding.Tests;

/// <summary>
/// REV-03: Testovi pravila vezivanja pokušaja za reviziju.
/// Svaki test ima jasan ulaz, pravilo i očekivanu odluku.
/// </summary>
[TestFixture]
public sealed class RevisionBindingPolicyTests
{
    // Fiksne vrijednosti, da se u testu odmah vidi šta je ulaz.
    private static readonly Guid RevisionId = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid LaboratoryId = Guid.Parse("b2000000-0000-4000-8000-000000000002");
    private static readonly Guid CourseId = Guid.Parse("c3000000-0000-4000-8000-000000000003");
    private static readonly string Hash = new('a', 64); // ispravan oblik SHA-256 hasha

    /// <summary>Pravi podatke o reviziji u traženom stanju.</summary>
    private static PublishedRevisionInfo Revision(RevisionState state, int? number = 1) =>
        new(RevisionId, LaboratoryId, CourseId, state, number, Hash);

    // POZITIVAN SCENARIO:
    // Objavljena revizija -> pokušaj se vezuje i veza pamti tačno tu reviziju i njen hash.
    [Test]
    public void Decide_WhenRevisionIsPublished_BindsToExactRevision()
    {
        var result = RevisionBindingPolicy.Decide(Revision(RevisionState.Published, number: 2));

        Assert.That(result, Is.TypeOf<BindingResult.Bound>());
        var binding = ((BindingResult.Bound)result).Binding;
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(BindingOutcome.Bound));
            Assert.That(binding.RevisionId, Is.EqualTo(RevisionId));
            Assert.That(binding.LaboratoryId, Is.EqualTo(LaboratoryId));
            Assert.That(binding.RevisionNumber, Is.EqualTo(2));
            Assert.That(binding.ContentHash, Is.EqualTo(Hash));
        });
    }

    // NEGATIVAN SCENARIO:
    // Nacrt, revizija na pregledu i arhivirana revizija -> vezivanje se odbija.
    // Jedan test, tri ulaza: svako stanje osim Published mora dati NotPublished.
    [TestCase(RevisionState.Draft)]
    [TestCase(RevisionState.InReview)]
    [TestCase(RevisionState.Archived)]
    public void Decide_WhenRevisionIsNotPublished_RejectsWithNotPublished(RevisionState state)
    {
        var result = RevisionBindingPolicy.Decide(Revision(state));

        Assert.That(result, Is.EqualTo(new BindingResult.Rejected(BindingOutcome.NotPublished)));
    }
}
