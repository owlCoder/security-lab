using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Integration;

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

    public static async Task<Session?> FindByTokenAsync(
        CustomWebApplicationFactory<Program> factory,
        string rawToken,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        return await sessions.FindByTokenAsync(rawToken, ct);
    }

    /// <summary>The statement of SessionManager.RevokeSession (IDN-07), committed at once on its own connection.</summary>
    public static async Task RevokeAsync(Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(TestDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE identity.sessions SET revoked_at = now() WHERE id = @id AND revoked_at IS NULL", connection);
        command.Parameters.AddWithValue("id", sessionId);
        await command.ExecuteNonQueryAsync();
    }
}
