using Platform.Api.Interfaces.Identity;

namespace Platform.Api.Services.Identity;

/// DEV STUB: id-jevi administratora iz konfiguracije (Stub:AdminIds, odvojeni zarezom).
public sealed class ConfigAdminDirectory(IConfiguration config) : IAdminDirectory
{
    private readonly HashSet<Guid> _ids = (config["Stub:AdminIds"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
        .Where(g => g != Guid.Empty)
        .ToHashSet();

    public bool IsAdministrator(Guid userId) => _ids.Contains(userId);
}
