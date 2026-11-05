
using System;
using NUnit.Framework;
using Platform.LabMetadata;

namespace Platform.LabMetadata.Tests
{
    [TestFixture]
    public class LaboratoryMetadataTests
    {
        [Test]
        public void Constructor_ShouldPreserveLaboratoryId()
        {
            var id = Guid.NewGuid();

            var metadata = new LaboratoryMetadata(
                id,
                "Learn access control",
                LaboratoryDifficulty.Easy,
                LaboratoryType.AccessControl,
                new[] { "security" }
            );

            Assert.That(metadata.LaboratoryId, Is.EqualTo(id));
        }

        [Test]
        public void Constructor_ShouldRejectInvalidDifficulty()
        {
            var invalidDifficulty = (LaboratoryDifficulty)999;

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LaboratoryMetadata(
                    Guid.NewGuid(),
                    "Test objective",
                    invalidDifficulty,
                    LaboratoryType.AccessControl,
                    new[] { "security" }
                )
            );
        }

        [Test]
        public void Constructor_ShouldRejectInvalidType()
        {
            var invalidType = (LaboratoryType)999;

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LaboratoryMetadata(
                    Guid.NewGuid(),
                    "Test objective",
                    LaboratoryDifficulty.Easy,
                    invalidType,
                    new[] { "security" }
                )
            );
        }

        [Test]
        public void Constructor_ShouldNormalizeTags()
        {
            var metadata = new LaboratoryMetadata(
                Guid.NewGuid(),
                "Test objective",
                LaboratoryDifficulty.Easy,
                LaboratoryType.AccessControl,
                new[] { " Security ", "MFA", "security", " " }
            );

            Assert.That(
                metadata.Tags,
                Is.EqualTo(new[] { "security", "mfa" })
            );
        }

        [Test]
        public void Constructor_ShouldRejectEmptyId()
        {
            Assert.Throws<ArgumentException>(() =>
                new LaboratoryMetadata(
                    Guid.Empty,
                    "Test objective",
                    LaboratoryDifficulty.Easy,
                    LaboratoryType.AccessControl,
                    new[] { "security" }
                )
            );
        }

        [Test]
        public void Constructor_ShouldRejectEmptyObjective()
        {
            Assert.Throws<ArgumentException>(() =>
                new LaboratoryMetadata(
                    Guid.NewGuid(),
                    " ",
                    LaboratoryDifficulty.Easy,
                    LaboratoryType.AccessControl,
                    new[] { "security" }
                )
            );
        }

        [Test]
        public void Constructor_ShouldRejectNullTags()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LaboratoryMetadata(
                    Guid.NewGuid(),
                    "Test objective",
                    LaboratoryDifficulty.Easy,
                    LaboratoryType.AccessControl,
                    null!
                )
            );
        }


        [Test]
        public void Constructor_ShouldPreserveMaliciousInputAsPlainText()
        {
            var maliciousInput = "<script>alert('XSS')</script>";

            var metadata = new LaboratoryMetadata(
                Guid.NewGuid(),
                maliciousInput,
                LaboratoryDifficulty.Easy,
                LaboratoryType.AccessControl,
                new[] { "security" }
            );

            Assert.That(metadata.Objective, Is.EqualTo(maliciousInput));
        }

    }
}
