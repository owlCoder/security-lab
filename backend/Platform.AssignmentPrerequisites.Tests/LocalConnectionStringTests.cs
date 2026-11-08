using Platform.AssignmentPrerequisites.Infrastructure.Persistence;

namespace Platform.AssignmentPrerequisites.Tests;

[TestFixture]
public class LocalConnectionStringTests
{
    [Test]
    public void Parse_ReplacesReferencesToEarlierKeys()
    {
        IReadOnlyDictionary<string, string> values = LocalConnectionString.Parse(
        [
            "# komentar",
            "POSTGRES_DB=securitylab",
            "POSTGRES_PASSWORD=\"tajna\"",
            "ConnectionStrings__Identity=Host=127.0.0.1;Database=${POSTGRES_DB};Password=${POSTGRES_PASSWORD}"
        ]);

        Assert.That(
            values[LocalConnectionString.IdentityKey],
            Is.EqualTo("Host=127.0.0.1;Database=securitylab;Password=tajna"));
    }

    [Test]
    public void Parse_IgnoresCommentsAndLinesWithoutKey()
    {
        IReadOnlyDictionary<string, string> values = LocalConnectionString.Parse(
        [
            "#KEY=value",
            "=value",
            "no separator"
        ]);

        Assert.That(values, Is.Empty);
    }

    [Test]
    public void ReadEnvFile_StopsAtRepositoryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "asn07-" + Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(root, "backend", "Module");
        Directory.CreateDirectory(nested);

        try
        {
            File.WriteAllText(Path.Combine(root, "docker-compose.yml"), "services: {}");
            File.WriteAllText(Path.Combine(root, ".env"), "ConnectionStrings__Assignments=Host=db");

            IReadOnlyDictionary<string, string> values = LocalConnectionString.ReadEnvFile(nested);

            Assert.That(values[LocalConnectionString.AssignmentsKey], Is.EqualTo("Host=db"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ReadEnvFile_WithoutRepositoryRoot_ReturnsNothing()
    {
        string folder = Path.Combine(Path.GetTempPath(), "asn07-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(Path.Combine(folder, ".env"), "ConnectionStrings__Assignments=Host=db");

            Assert.That(LocalConnectionString.ReadEnvFile(folder), Is.Empty);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
