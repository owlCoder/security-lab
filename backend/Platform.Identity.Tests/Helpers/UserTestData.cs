using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;

namespace Platform.Identity.Tests.Helpers;

public static class UserTestData
{
    public static async Task<UserAccount> AddAsync(
        CustomWebApplicationFactory<Program> factory,
        string email,
        string password,
        AccountStatus status = AccountStatus.Active,
        AccountRole role = AccountRole.Student,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();

        var user = new UserAccount(Guid.NewGuid(), email, status, role);
        user.SetPasswordHash(passwordHasher.Hash(password));

        db.Accounts.Add(user);
        await db.SaveChangesAsync(ct);

        return user;
    }

    /// <summary>
    /// IDN-05: locks the account in the store until <paramref name="lockedFor"/> from now (15 minutes by default),
    /// without failed logins, so the per-IP login limiter plays no part in the arrangement.
    /// </summary>
    public static async Task LockAsync(
        CustomWebApplicationFactory<Program> factory,
        Guid id,
        int failedLogins = 0,
        TimeSpan? lockedFor = null,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        var lockoutEnd = DateTimeOffset.UtcNow + (lockedFor ?? TimeSpan.FromMinutes(15));

        var updated = await db.Accounts
            .Where(user => user.Id == id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(user => user.LockoutEnd, lockoutEnd)
                .SetProperty(user => user.FailedLoginCount, failedLogins), ct);

        if (updated != 1)
            throw new InvalidOperationException($"Account {id} does not exist.");
    }

    public static async Task<UserAccount?> GetAsync(
        CustomWebApplicationFactory<Program> factory,
        Guid id,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        return await db.Accounts.FirstOrDefaultAsync(user => user.Id == id, ct);
    }

    public static async Task<UserAccount?> GetByEmailAsync(
        CustomWebApplicationFactory<Program> factory,
        string email,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        return await db.Accounts.FirstOrDefaultAsync(user => user.Email == email, ct);
    }
}
