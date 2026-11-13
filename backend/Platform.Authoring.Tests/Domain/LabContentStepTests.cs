using NUnit.Framework;
using Platform.Authoring.Domain;

namespace Platform.Authoring.Tests.Domain;

[TestFixture]
public class LabContentStepTests
{
    [Test]
    public void Create_with_valid_steps_succeeds()
    {
        var steps = new[]
        {
            LabStep.Create(1, "Step One", "Do this first."),
            LabStep.Create(2, "Step Two", "Do this second.")
        };

        var content = LabContent.Create("Lab", "Goal", LabType.AccessControl, steps);

        Assert.That(content.Steps.Count, Is.EqualTo(2));
        Assert.That(content.Steps[0].SequenceOrder, Is.EqualTo(1));
        Assert.That(content.Steps[1].SequenceOrder, Is.EqualTo(2));
        Assert.That(content.Steps[0].Title, Is.EqualTo("Step One"));
    }

    [Test]
    public void Steps_with_gapped_sequence_are_rejected()
    {
        var steps = new[]
        {
            LabStep.Create(1, "Step One", "Instruction"),
            LabStep.Create(3, "Step Three", "Instruction") // Missing 2
        };

        var ex = Assert.Throws<DomainRuleException>(() =>
        {
            LabContent.Create("Lab", "Goal", LabType.AccessControl, steps);
        });

        Assert.That(ex.Code, Is.EqualTo(AuthoringErrorCodes.InvalidStepSequence));
    }

    [Test]
    public void Steps_with_duplicate_sequence_are_rejected()
    {
        var steps = new[]
        {
            LabStep.Create(1, "Step One", "Instruction"),
            LabStep.Create(1, "Step Duplicate", "Instruction") // Duplicate 1
        };

        var ex = Assert.Throws<DomainRuleException>(() =>
        {
            LabContent.Create("Lab", "Goal", LabType.AccessControl, steps);
        });

        Assert.That(ex.Code, Is.EqualTo(AuthoringErrorCodes.InvalidStepSequence));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Step_sequence_must_start_from_positive_integer(int invalidOrder)
    {
        var ex = Assert.Throws<DomainRuleException>(() =>
        {
            LabStep.Create(invalidOrder, "Title", "Instruction");
        });

        Assert.That(ex.Code, Is.EqualTo(AuthoringErrorCodes.InvalidStepSequence));
    }
    [Test]
    public void Step_with_empty_title_is_rejected()
    {
        var ex = Assert.Throws<DomainRuleException>(() =>
            LabStep.Create(1, "", "Do this first."));

        Assert.That(ex!.Code, Is.EqualTo(AuthoringErrorCodes.InvalidStepTitle));
    }

    [Test]
    public void Step_with_whitespace_title_is_rejected()
    {
        var ex = Assert.Throws<DomainRuleException>(() =>
            LabStep.Create(1, "   ", "Do this first."));

        Assert.That(ex!.Code, Is.EqualTo(AuthoringErrorCodes.InvalidStepTitle));
    }

    [Test]
    public void Step_with_empty_instruction_is_rejected()
    {
        var ex = Assert.Throws<DomainRuleException>(() =>
            LabStep.Create(1, "Step One", ""));

        Assert.That(ex!.Code, Is.EqualTo(AuthoringErrorCodes.InvalidStepInstruction));
    }

    [Test]
    public void Step_with_whitespace_instruction_is_rejected()
    {
        var ex = Assert.Throws<DomainRuleException>(() =>
            LabStep.Create(1, "Step One", "   "));

        Assert.That(ex!.Code, Is.EqualTo(AuthoringErrorCodes.InvalidStepInstruction));
    }

    [Test]
    public void Step_title_exceeding_max_length_is_rejected()
    {
        var title = new string('A', LabStep.MaxTitleLength + 1);

        var ex = Assert.Throws<DomainRuleException>(() =>
            LabStep.Create(1, title, "Do this first."));

        Assert.That(ex!.Code, Is.EqualTo(AuthoringErrorCodes.InvalidStepTitle));
    }

    [Test]
    public void Step_instruction_exceeding_max_length_is_rejected()
    {
        var instruction = new string('A', LabStep.MaxInstructionLength + 1);

        var ex = Assert.Throws<DomainRuleException>(() =>
            LabStep.Create(1, "Step One", instruction));

        Assert.That(ex!.Code, Is.EqualTo(AuthoringErrorCodes.InvalidStepInstruction));
    }
    [Test]
    public void Steps_with_null_entry_are_rejected()
    {
        LabStep?[] steps =
        {
        LabStep.Create(1, "Step One", "Do this first."),
        null
    };

        var ex = Assert.Throws<DomainRuleException>(() =>
            LabContent.Create("Lab", "Goal", LabType.AccessControl, steps!));

        Assert.That(ex!.Code, Is.EqualTo(AuthoringErrorCodes.InvalidStepSequence));
    }

    [Test]
    public void Steps_are_stored_in_sequence_order()
    {
        var steps = new[]
        {
        LabStep.Create(3, "Step Three", "Do this third."),
        LabStep.Create(1, "Step One", "Do this first."),
        LabStep.Create(2, "Step Two", "Do this second.")
    };

        var content = LabContent.Create("Lab", "Goal", LabType.AccessControl, steps);

        Assert.That(content.Steps.Select(s => s.SequenceOrder),
            Is.EqualTo(new[] { 1, 2, 3 }));
    }
}