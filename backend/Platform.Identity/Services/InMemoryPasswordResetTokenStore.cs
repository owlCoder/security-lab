using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Services;

public sealed class InMemoryPasswordResetTokenStore
    : IPasswordResetTokenStore
{
    private readonly object _sync = new();

    private readonly Dictionary<string, PasswordResetTokenSnapshot> _tokens =
        new(StringComparer.Ordinal);

    public Task<bool> TrySaveAsync(
        PasswordResetToken token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!token.IsValid() ||
                token.IsUsed ||
                _tokens.ContainsKey(token.TokenHash))
            {
                return Task.FromResult(false);
            }

            var snapshot = new PasswordResetTokenSnapshot(
                token.Id,
                token.UserId,
                token.TokenHash,
                token.CreatedAt,
                token.ExpiresAt,
                token.IsUsed,
                token.UsedAt);

            _tokens.Add(snapshot.TokenHash, snapshot);

            return Task.FromResult(true);
        }
    }

    public Task<PasswordResetTokenLookupResult> FindByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (tokenHash.AsSpan().Trim().IsEmpty ||
                !_tokens.TryGetValue(tokenHash, out var snapshot))
            {
                return Task.FromResult<PasswordResetTokenLookupResult>(
                    new PasswordResetTokenLookupResult.NotFound());
            }

            return Task.FromResult<PasswordResetTokenLookupResult>(
                new PasswordResetTokenLookupResult.Found(snapshot));
        }
    }

    public Task<bool> TryConsumeAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (tokenHash.AsSpan().Trim().IsEmpty ||
                !_tokens.TryGetValue(tokenHash, out var snapshot))
            {
                return Task.FromResult(false);
            }

            if (snapshot.IsUsed ||
                now < snapshot.CreatedAt ||
                now >= snapshot.ExpiresAt)
            {
                return Task.FromResult(false);
            }

            _tokens[tokenHash] = snapshot with
            {
                IsUsed = true,
                UsedAt = now
            };

            return Task.FromResult(true);
        }
    }
}
