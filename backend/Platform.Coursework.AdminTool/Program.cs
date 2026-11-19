using Platform.Api.Modules.Administration.Persistence;

namespace Platform.Coursework.AdminTool;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine("ADM-08 operator tool: migrate | grant-runtime <existing-role>");
            Console.WriteLine("Set COURSEWORK_MIGRATION_CONNECTION outside source control. This tool never provisions accounts, courses or root grants.");
            return 0;
        }
        if (!((args[0] == "migrate" && args.Length == 1) || (args[0] == "grant-runtime" && args.Length == 2)))
        { Console.Error.WriteLine("Use migrate or grant-runtime <existing-role>."); return 2; }
        var connection = Environment.GetEnvironmentVariable("COURSEWORK_MIGRATION_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        { Console.Error.WriteLine("COURSEWORK_MIGRATION_CONNECTION is required for this operator action."); return 2; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            if (args[0] == "migrate") await AdministrationSchema.MigrateAsync(connection, timeout.Token);
            else await AdministrationSchema.GrantRuntimeRoleAsync(connection, args[1], timeout.Token);
            Console.WriteLine("ADM-08 operator action completed.");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("ADM-08 operator action failed. Check operator database access and schema; credentials are not printed.");
            return 1;
        }
    }
}
