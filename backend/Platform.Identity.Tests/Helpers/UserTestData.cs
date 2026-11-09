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

    public static async Task<UserAccount?> GetAsync(
        CustomWebApplicationFactory<Program> factory,
        Guid id,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        return await db.Accounts.FirstOrDefaultAsync(user => user.Id == id, ct);
    }
}
