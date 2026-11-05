using Microsoft.Extensions.Configuration;
using Platform.Authoring.Application;

namespace Platform.Authoring.Infrastructure;

/// <summary>
/// DEV STUB: author account ids come from configuration (<c>Authoring:AuthorAccountIds</c>, a list of GUIDs),
/// the same approach Identity uses for administrators. Entries that are not valid GUIDs are ignored,
/// so a typo can only remove access, never grant it. Replace with the course-role contract of the ACC modules.
/// </summary>
public sealed class ConfigAuthorDirectory(IConfiguration configuration) : IAuthorDirectory
{
    private readonly HashSet<Guid> _authors = (configuration.GetSection("Authoring:AuthorAccountIds").Get<string[]>() ?? [])
        .Select(raw => Guid.TryParse(raw, out var id) ? id : Guid.Empty)
        .Where(id => id != Guid.Empty)
        .ToHashSet();

    public bool IsAuthor(Guid userId) => userId != Guid.Empty && _authors.Contains(userId);
}
