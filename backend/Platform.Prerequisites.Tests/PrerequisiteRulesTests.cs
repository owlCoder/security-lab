using NUnit.Framework;
using Platform.Prerequisites.Domain;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class PrerequisiteRulesTests
{
    [Test]
    public void IsDuplicate_SamePairExists_ReturnsTrue()
    {
        Guid labA = Guid.NewGuid();
        Guid labB = Guid.NewGuid();
        var existing = new[] { new LabPrerequisite(labA, labB) };

        bool result = PrerequisiteRules.IsDuplicate(
            existing,
            new LabPrerequisite(labA, labB));

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsDuplicate_DifferentPair_ReturnsFalse()
    {
        Guid labA = Guid.NewGuid();
        Guid labB = Guid.NewGuid();
        Guid labC = Guid.NewGuid();
        var existing = new[] { new LabPrerequisite(labA, labB) };

        bool result = PrerequisiteRules.IsDuplicate(
            existing,
            new LabPrerequisite(labA, labC));

        Assert.That(result, Is.False);
    }

    [Test]
    public void WouldCreateCycle_TwoLaboratories_ReturnsTrue()
    {
        Guid labA = Guid.NewGuid();
        Guid labB = Guid.NewGuid();
        var existing = new[] { new LabPrerequisite(labA, labB) };

        bool result = PrerequisiteRules.WouldCreateCycle(
            existing,
            new LabPrerequisite(labB, labA));

        Assert.That(result, Is.True);
    }

    [Test]
    public void WouldCreateCycle_ThreeLaboratories_ReturnsTrue()
    {
        Guid labA = Guid.NewGuid();
        Guid labB = Guid.NewGuid();
        Guid labC = Guid.NewGuid();
        var existing = new[]
        {
            new LabPrerequisite(labA, labB),
            new LabPrerequisite(labB, labC)
        };

        bool result = PrerequisiteRules.WouldCreateCycle(
            existing,
            new LabPrerequisite(labC, labA));

        Assert.That(result, Is.True);
    }

    [Test]
    public void WouldCreateCycle_NormalChain_ReturnsFalse()
    {
        Guid labA = Guid.NewGuid();
        Guid labB = Guid.NewGuid();
        Guid labC = Guid.NewGuid();
        var existing = new[] { new LabPrerequisite(labA, labB) };

        bool result = PrerequisiteRules.WouldCreateCycle(
            existing,
            new LabPrerequisite(labB, labC));

        Assert.That(result, Is.False);
    }

    [Test]
    public void WouldCreateCycle_SharedPrerequisite_ReturnsFalse()
    {
        Guid labA = Guid.NewGuid();
        Guid labB = Guid.NewGuid();
        Guid labC = Guid.NewGuid();
        Guid labD = Guid.NewGuid();
        var existing = new[]
        {
            new LabPrerequisite(labA, labB),
            new LabPrerequisite(labA, labC),
            new LabPrerequisite(labB, labD)
        };

        bool result = PrerequisiteRules.WouldCreateCycle(
            existing,
            new LabPrerequisite(labC, labD));

        Assert.That(result, Is.False);
    }
}
