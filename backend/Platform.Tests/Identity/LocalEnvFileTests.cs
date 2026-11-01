using Microsoft.Extensions.Configuration;
using Platform.Identity.Data;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("ADM-04")]
public class LocalEnvFileTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void Parse_SkipsCommentsAndLinesWithoutKey()
    {
        var values = LocalEnvFile.Parse(["# comment", "", "NOT_A_PAIR", "=no-key", "KEY=value"]);

        Assert.That(values, Has.Count.EqualTo(1));
        Assert.That(values["KEY"], Is.EqualTo("value"));
    }

    [Test]
    public void Parse_KeepsEqualsSignsAndRemovesQuotes()
    {
        var values = LocalEnvFile.Parse(["CONNECTION=Host=127.0.0.1;Port=5432", "QUOTED='a b'", "DOUBLE=\"c\""]);

        Assert.That(values["CONNECTION"], Is.EqualTo("Host=127.0.0.1;Port=5432"));
        Assert.That(values["QUOTED"], Is.EqualTo("a b"));
        Assert.That(values["DOUBLE"], Is.EqualTo("c"));
    }

    [Test]
    public void Parse_ExpandsReferenceToEarlierKey()
    {
        var values = LocalEnvFile.Parse(["USER_NAME=platform", "CONNECTION=Username=${USER_NAME}"]);

        Assert.That(values["CONNECTION"], Is.EqualTo("Username=platform"));
    }

    [Test]
    public void Parse_UnknownReferenceBecomesEmpty()
    {
        var values = LocalEnvFile.Parse(["CONNECTION=Password=${MISSING}"]);

        Assert.That(values["CONNECTION"], Is.EqualTo("Password="));
    }

    [Test]
    public void Read_FindsFileAtRepositoryRoot_FromNestedDirectory()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "backend", "bin")).FullName;
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "");
        File.WriteAllText(Path.Combine(_root, ".env"), "KEY=value");

        Assert.That(LocalEnvFile.Read(nested)["KEY"], Is.EqualTo("value"));
    }

    [Test]
    public void Read_IgnoresEnvFileAboveRepositoryRoot()
    {
        var repository = Directory.CreateDirectory(Path.Combine(_root, "repository")).FullName;
        File.WriteAllText(Path.Combine(repository, "docker-compose.yml"), "");
        File.WriteAllText(Path.Combine(_root, ".env"), "KEY=value");

        Assert.That(LocalEnvFile.Read(repository), Is.Empty);
    }

    [Test]
    public void Read_OutsideRepository_ReturnsNothing()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), "KEY=value");

        Assert.That(LocalEnvFile.Read(_root), Is.Empty);
    }

    [Test]
    public void AddLocalEnvFile_MapsDoubleUnderscoreToConfigurationSection()
    {
        var previous = Directory.GetCurrentDirectory();
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "");
        File.WriteAllText(Path.Combine(_root, ".env"), "ConnectionStrings__Identity=Host=db");

        try
        {
            Directory.SetCurrentDirectory(_root);
            var configuration = new ConfigurationBuilder().AddLocalEnvFile().Build();

            Assert.That(configuration.GetConnectionString("Identity"), Is.EqualTo("Host=db"));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Test]
    public void AddLocalEnvFile_RealEnvironmentVariableWins()
    {
        const string key = "LocalEnvFileTests__Key";
        var previous = Directory.GetCurrentDirectory();
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "");
        File.WriteAllText(Path.Combine(_root, ".env"), $"{key}=from-file");

        try
        {
            Environment.SetEnvironmentVariable(key, "from-environment");
            Directory.SetCurrentDirectory(_root);
            var configuration = new ConfigurationBuilder().AddLocalEnvFile().Build();

            Assert.That(configuration["LocalEnvFileTests:Key"], Is.Null);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Environment.SetEnvironmentVariable(key, null);
        }
    }
}
