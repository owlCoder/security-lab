using System.Text.RegularExpressions;

namespace Platform.AssignmentPrerequisites.Infrastructure.Persistence;

public static partial class LocalConnectionString
{
    public const string AssignmentsKey = "ConnectionStrings__Assignments";
    public const string IdentityKey = "ConnectionStrings__Identity";

    private const string EnvFileName = ".env";
    private const string RepositoryRootMarker = "docker-compose.yml";

    public static string? Find(string? startDirectory = null)
    {
        foreach (string key in new[] { AssignmentsKey, IdentityKey })
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment;
            }
        }

        IReadOnlyDictionary<string, string> envFile = ReadEnvFile(startDirectory ?? Directory.GetCurrentDirectory());

        foreach (string key in new[] { AssignmentsKey, IdentityKey })
        {
            if (envFile.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    internal static IReadOnlyDictionary<string, string> ReadEnvFile(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, RepositoryRootMarker)))
            {
                continue;
            }

            string path = Path.Combine(directory.FullName, EnvFileName);
            return File.Exists(path)
                ? Parse(File.ReadLines(path))
                : new Dictionary<string, string>();
        }

        return new Dictionary<string, string>();
    }

    internal static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in lines)
        {
            int separator = line.IndexOf('=');
            if (separator <= 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim().Trim('"', '\'');

            values[key] = Reference().Replace(
                value,
                match => values.GetValueOrDefault(match.Groups[1].Value) ?? string.Empty);
        }

        return values;
    }

    [GeneratedRegex(@"\$\{(\w+)\}")]
    private static partial Regex Reference();
}
