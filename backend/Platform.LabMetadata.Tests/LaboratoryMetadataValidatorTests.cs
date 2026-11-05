
using System;
using NUnit.Framework;
using Platform.LabMetadata;

namespace Platform.LabMetadata.Tests;

[TestFixture]
public class LaboratoryMetadataValidatorTests
{
    private readonly LaboratoryMetadataValidator _validator = new();

    private MetadataValidationResult Validate(
        string? objective = "Learn access control",
        LaboratoryDifficulty difficulty = LaboratoryDifficulty.Easy,
        LaboratoryType type = LaboratoryType.AccessControl)
    {
        return _validator.Validate(
            Guid.NewGuid(),
            objective,
            difficulty,
            type,
            new[] { "security" }
        );
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Validate_EmptyObjective_ReturnsFieldError(
        string? objective)
    {
        var result = Validate(objective);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.ContainsKey("objective"), Is.True);
    }

    [Test]
    public void Validate_OverlongObjective_ReturnsFieldError()
    {
        var objective = new string(
            'a',
            LaboratoryMetadataValidator.MaxObjectiveLength + 1
        );

        var result = Validate(objective);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.ContainsKey("objective"), Is.True);
    }

    [Test]
    public void Constructor_OverlongObjective_Throws()
    {
        var objective = new string(
            'a',
            LaboratoryMetadataValidator.MaxObjectiveLength + 1
        );

        Assert.Throws<ArgumentException>(() =>
            new LaboratoryMetadata(
                Guid.NewGuid(),
                objective,
                LaboratoryDifficulty.Easy,
                LaboratoryType.AccessControl,
                new[] { "security" }
            ));
    }

    [Test]
    public void Validate_InvalidDifficulty_ReturnsFieldError()
    {
        var result = Validate(
            difficulty: (LaboratoryDifficulty)999
        );

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.ContainsKey("difficulty"), Is.True);
    }

    [Test]
    public void Validate_InvalidType_ReturnsFieldError()
    {
        var result = Validate(
            type: (LaboratoryType)999
        );

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.ContainsKey("type"), Is.True);
    }

    [Test]
    public void Validate_MultipleInvalidFields_ReturnsAllErrors()
    {
        var result = Validate(
            objective: "",
            difficulty: (LaboratoryDifficulty)999
        );

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.ContainsKey("objective"), Is.True);
        Assert.That(result.Errors.ContainsKey("difficulty"), Is.True);
        Assert.That(result.Errors.Count, Is.EqualTo(2));
    }

    [Test]
    public void Validate_ValidMetadata_ReturnsNoErrors()
    {
        var result = Validate();

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public void Validate_ScriptLikeObjective_RemainsValidText()
    {
        var objective = "<script>alert('XSS')</script>";

        var result = Validate(objective);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public void Validate_EmptyLaboratoryId_ReturnsFieldError()
    {
        var result = _validator.Validate(
            Guid.Empty,
            "Learn access control",
            LaboratoryDifficulty.Easy,
            LaboratoryType.AccessControl,
            new[] { "security" }
        );

        Assert.That(result.Errors.ContainsKey("laboratoryId"), Is.True);
    }

    [Test]
    public void Validate_NullTags_ReturnsFieldError()
    {
        var result = _validator.Validate(
            Guid.NewGuid(),
            "Learn access control",
            LaboratoryDifficulty.Easy,
            LaboratoryType.AccessControl,
            null
        );

        Assert.That(result.Errors.ContainsKey("tags"), Is.True);
    }


    [Test]
    public void Validate_MaximumLengthObjective_IsValid()
    {
        var objective = new string(
            'a',
            LaboratoryMetadataValidator.MaxObjectiveLength
        );

        var result = Validate(objective);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Errors, Is.Empty);
    }


}
