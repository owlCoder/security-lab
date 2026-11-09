using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;

namespace Platform.Identity.Tests.Helpers;

public static class SessionTestData
{
    public static async Task<(string RawToken, Session Session)> CreateAsync(
        CustomWebApplicationFactory<Program> factory,
        Guid userId,
        AuthLevel level = AuthLevel.Active,
        TimeSpan? lifetime = null,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();

        return await sessions.CreateAsync(
            userId,
            level,
            lifetime ?? TimeSpan.FromHours(8),
            ct);
    }

    public static async Task<Session?> GetAsync(
        CustomWebApplicationFactory<Program> factory,
        Guid id,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        return await db.Set<Session>().FirstOrDefaultAsync(session => session.Id == id, ct);
    }
}
