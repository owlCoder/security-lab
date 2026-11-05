using NUnit.Framework;
using Platform.Prerequisites.Domain;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class LabPrerequisiteTests
{
    [Test]
    public void Constructor_ValidLaboratories_SavesBothIds()
    {
        Guid laboratoryId = Guid.NewGuid();
        Guid requiredId = Guid.NewGuid();

        var prerequisite = new LabPrerequisite(laboratoryId, requiredId);

        Assert.Multiple(() =>
        {
            Assert.That(prerequisite.LaboratoryId, Is.EqualTo(laboratoryId));
            Assert.That(prerequisite.RequiredLaboratoryId, Is.EqualTo(requiredId));
        });
    }

    [Test]
    public void Constructor_SameLaboratory_IsRejected()
    {
        Guid laboratoryId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(
            () => new LabPrerequisite(laboratoryId, laboratoryId));
    }

    [Test]
    public void Constructor_EmptyId_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new LabPrerequisite(Guid.Empty, Guid.NewGuid()));
    }
}