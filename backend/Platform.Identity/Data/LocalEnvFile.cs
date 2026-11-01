using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Platform.Identity.Data;

/// <summary>
/// Reads the git-ignored .env next to docker-compose.yml, so local setup needs no dotnet user-secrets.
/// For local development only: deployed environments use real environment variables.
/// </summary>
public static partial class LocalEnvFile
{
    private const string FileName = ".env";
    private const string RepositoryRootMarker = "docker-compose.yml";

    /// <summary>A variable that is really set in the environment wins over the same key in .env.</summary>
    public static IConfigurationBuilder AddLocalEnvFile(this IConfigurationBuilder builder) =>
        builder.AddInMemoryCollection(Read(Directory.GetCurrentDirectory())
            .Where(pair => Environment.GetEnvironmentVariable(pair.Key) is null)
            .ToDictionary(pair => pair.Key.Replace("__", ConfigurationPath.KeyDelimiter), pair => pair.Value));

    /// <summary>Stops at the repository root, so a .env from some parent folder is never picked up.</summary>
    public static IReadOnlyDictionary<string, string?> Read(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, RepositoryRootMarker)))
                continue;

            var path = Path.Combine(directory.FullName, FileName);
            return File.Exists(path) ? Parse(File.ReadLines(path)) : new Dictionary<string, string?>();
        }

        return new Dictionary<string, string?>();
    }

    /// <summary>Supports KEY=value, # comments, surrounding quotes and ${KEY} of a key defined earlier.</summary>
    public static IReadOnlyDictionary<string, string?> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || line.TrimStart().StartsWith('#'))
                continue;

            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            values[line[..separator].Trim()] = Reference().Replace(value, match => values.GetValueOrDefault(match.Groups[1].Value) ?? "");
        }

        return values;
    }

    [GeneratedRegex(@"\$\{(\w+)\}")]
    private static partial Regex Reference();
}
