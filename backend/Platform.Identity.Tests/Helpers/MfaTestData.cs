using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces.Mfa;
using Platform.Identity.Persistence;

namespace Platform.Identity.Tests.Helpers;

public static class MfaTestData
{
    public static async Task<MfaCredentials> AddEnabledAsync(
        CustomWebApplicationFactory<Program> factory,
        Guid userId,
        string secret,
        long? lastTotpTimeStep = null,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ITotpSecretProtector>();
        var credentials = new MfaCredentials(userId)
        {
            MfaEnabled = true,
            TotpSecretCiphertext = protector.Protect(secret),
            TotpSecretKeyVersion = 1,
            LastTotpTimeStep = lastTotpTimeStep
        };

        db.MfaCredentials.Add(credentials);
        await db.SaveChangesAsync(ct);
        return credentials;
    }

    public static async Task<MfaCredentials> AddPendingAsync(
        CustomWebApplicationFactory<Program> factory,
        Guid userId,
        string secret,
        DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ITotpSecretProtector>();
        var credentials = new MfaCredentials(userId)
        {
            PendingTotpSecretCiphertext = protector.Protect(secret),
            PendingTotpSecretKeyVersion = 1,
            PendingEnrollmentExpiresAt = expiresAt
        };

        db.MfaCredentials.Add(credentials);
        await db.SaveChangesAsync(ct);
        return credentials;
    }
}
