using NUnit.Framework;
using Platform.Competencies.Domain;

namespace Platform.Competencies.Tests;

[TestFixture]
public sealed class CompetencyTests
{
    [Test]
    public void Catalog_HasStableUniqueIdsAndNames()
    {
        Assert.That(BuiltInCompetencies.All.Select(x => x.Id), Is.EqualTo(new[]
        {
            Guid.Parse("4b677e63-204f-4688-a0eb-a339c343f101"),
            Guid.Parse("4b677e63-204f-4688-a0eb-a339c343f102"),
            Guid.Parse("4b677e63-204f-4688-a0eb-a339c343f103"),
        }));
        Assert.That(BuiltInCompetencies.All.Select(x => x.Name).Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public void Competency_KeepsIdentityAndNormalizesOptionalDescription()
    {
        Guid id = Guid.NewGuid();
        var competency = new Competency(id, "  Analiza logova  ", "  Opis  ");
        Assert.That(competency.Id, Is.EqualTo(id));
        Assert.That(competency.Name, Is.EqualTo("Analiza logova"));
        Assert.That(competency.Description, Is.EqualTo("Opis"));
        Assert.That(new Competency(id, "Name", "  ").Description, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void Competency_RejectsMissingName(string? name) =>
        Assert.Throws<ArgumentException>(() => new Competency(Guid.NewGuid(), name!));

    [Test]
    public void Competency_RejectsEmptyIdOrOverlongFields()
    {
        Assert.Throws<ArgumentException>(() => new Competency(Guid.Empty, "Name"));
        Assert.Throws<ArgumentException>(() => new Competency(Guid.NewGuid(), new string('n', Competency.MaxNameLength + 1)));
        Assert.Throws<ArgumentException>(() => new Competency(Guid.NewGuid(), "Name", new string('d', Competency.MaxDescriptionLength + 1)));
    }

    [Test]
    public void Link_ReferencesStableLaboratoryAndCompetencyIds()
    {
        Guid laboratoryId = Guid.NewGuid();
        var link = new LaboratoryCompetency(laboratoryId, BuiltInCompetencies.AccessControlId);
        Assert.That(link.LaboratoryId, Is.EqualTo(laboratoryId));
        Assert.That(link.CompetencyId, Is.EqualTo(BuiltInCompetencies.AccessControlId));
        Assert.Throws<ArgumentException>(() => new LaboratoryCompetency(Guid.Empty, link.CompetencyId));
        Assert.Throws<ArgumentException>(() => new LaboratoryCompetency(laboratoryId, Guid.Empty));
    }
}
