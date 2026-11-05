using Platform.Api.Modules.Laboratories.Domain;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LabRevisionTests
{
    [Test]
    public void Create_KeepsLaboratoryIdAndNumber()
    {
        Guid laboratoryId = Guid.NewGuid();

        var revision = new LabRevision(laboratoryId, 1);

        Assert.That(revision.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(revision.LaboratoryId, Is.EqualTo(laboratoryId));
        Assert.That(revision.Number, Is.EqualTo(1));
    }

    [Test]
    public void Create_WithEmptyLaboratoryId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LabRevision(Guid.Empty, 1));
    }

    [Test]
    public void Create_WithEmptyRevisionId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LabRevision(Guid.Empty, Guid.NewGuid(), 1));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_WithNumberBelowOne_Throws(int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LabRevision(Guid.NewGuid(), number));
    }
}
