using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.LabVisibility.Infrastructure.Persistence;

// Odluka EF ili InMemory. Connection string dolazi samo iz user-secrets ili promenljive
// okruženja (ConnectionStrings__LabVisibility), nikad iz appsettings ni iz Gita.
public static class LabVisibilityDatabase
{
    public const string ConnectionStringName = "LabVisibility";

    public static string? GetConnectionString(IServiceProvider services) =>
        services.GetService<IConfiguration>()?.GetConnectionString(ConnectionStringName);

    // Bez connection string-a (CI, testovi, kolege bez Dockera) modul radi sa InMemory skladištem.
    public static bool IsConfigured(IServiceProvider services) =>
        !string.IsNullOrWhiteSpace(GetConnectionString(services));
}
