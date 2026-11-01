using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.Identity;
using Platform.Identity.Data;
using Platform.Identity.Domain;
using Platform.Identity.Services;

namespace Platform.Api.Services.Identity;

/// <summary>The account model has no role yet, so this role only describes the account and is not persisted.</summary>
public enum DevelopmentRole
{
    Student,
    Teacher,
    Administrator
}

public sealed record DevelopmentAccount(Guid Id, string Email, DevelopmentRole Role, AccountStatus Status);
public sealed record SeedResult(int Added, int PasswordsSet);

public static class DevelopmentAccountSeeder
{
    // Set with: dotnet user-secrets set "Seed:DevelopmentPassword" "..." --project backend/Platform.Identity.
    public const string PasswordConfigKey = "Seed:DevelopmentPassword";
    // Must match Stub:AdminIds in appsettings.Development.json.
    public static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IReadOnlyList<DevelopmentAccount> All { get; } =
    [
        new DevelopmentAccount(AdminId, "admin@example.com", DevelopmentRole.Administrator, AccountStatus.Active),
        new DevelopmentAccount(Guid.Parse("22222222-2222-2222-2222-222222222222"), "student@example.com", DevelopmentRole.Student, AccountStatus.Active),
        new DevelopmentAccount(Guid.Parse("44444444-4444-4444-4444-444444444444"), "teacher@example.com", DevelopmentRole.Teacher, AccountStatus.Active),
        new DevelopmentAccount(Guid.Parse("55555555-5555-5555-5555-555555555555"), "blocked@example.com", DevelopmentRole.Student, AccountStatus.Blocked)
    ];

    // BCrypt hashing of four accounts takes a few hundred milliseconds each.
    private static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(15);

    public static async Task SeedDevelopmentAccountsAsync(this WebApplication app, CancellationToken ct = default)
    {
        RequireDevelopment(app.Environment);

        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DevelopmentAccountSeeder));

        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(IdentityModule.ConnectionStringName)))
        {
            logger.LogInformation("Identity connection string is not set, development accounts are skipped. See docs/DEVELOPMENT.md.");
            return;
        }

        var password = app.Configuration[PasswordConfigKey];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation(
                "{Key} is not set, development accounts are skipped. See docs/DEVELOPMENT.md.", PasswordConfigKey);
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(DatabaseTimeout);
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var result = await SeedAsync(context, hasher, password, app.Environment, timeout.Token);

            logger.LogInformation(
                "Development accounts: {Added} added, {PasswordsSet} passwords set, {Total} defined.",
                result.Added, result.PasswordsSet, All.Count);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Development accounts were not seeded: {Message} Is the database running and migrated? See docs/DEVELOPMENT.md.",
                ex.Message);
        }
    }

    public static async Task<SeedResult> SeedAsync(
        PlatformIdentityDbContext context,
        IPasswordHasher hasher,
        string password,
        IHostEnvironment environment,
        CancellationToken ct = default)
    {
        RequireDevelopment(environment);

        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length > CredentialVerifier.MaxPasswordLength)
            throw new ArgumentException(
                $"Password must be no more than {CredentialVerifier.MaxPasswordLength} characters.",
                nameof(password));

        if ((await context.Database.GetPendingMigrationsAsync(ct)).Any())
            throw new InvalidOperationException("Identity migrations are not applied. Run 'dotnet ef database update' first.");

        var emails = All.Select(account => account.Email).ToArray();
        var accounts = await context.Accounts
            .Where(account => emails.Contains(account.Email))
            .ToDictionaryAsync(account => account.Email, StringComparer.Ordinal, ct);
        var added = 0;
        var passwordsSet = 0;

        foreach (var definition in All)
        {
            if (!accounts.TryGetValue(definition.Email, out var account))
            {
                account = new UserAccount(definition.Id, definition.Email, definition.Status);
                context.Accounts.Add(account);
                accounts.Add(definition.Email, account);
                added++;
            }

            if (account.PasswordHash is null)
            {
                account.SetPasswordHash(hasher.Hash(password));
                passwordsSet++;
            }
        }

        await context.SaveChangesAsync(ct);
        return new SeedResult(added, passwordsSet);
    }

    public static void RequireDevelopment(IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("Development accounts are seeded only in the Development environment.");
    }
}
