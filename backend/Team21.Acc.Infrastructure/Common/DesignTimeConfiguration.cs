using Microsoft.Extensions.Configuration;

namespace Team21.Acc.Infrastructure.Common;

/// <summary>
/// Connection string for dotnet ef. User secrets first, then the ConnectionStrings__Team21
/// env variable.
/// </summary>
internal static class DesignTimeConfiguration
{
    public static string ConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(DesignTimeConfiguration).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        return configuration.GetConnectionString(TeamDbContextRegistration.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{TeamDbContextRegistration.ConnectionStringName}' not found. " +
                "Set it with dotnet user-secrets, see docs/team-21/README.md.");
    }
}
