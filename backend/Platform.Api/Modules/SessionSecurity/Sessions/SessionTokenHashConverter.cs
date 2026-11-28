using System.Buffers;
using System.Security.Cryptography;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Sessions;

/// <summary>
/// IDN-11 (D2): converts a session token hash between the upper-case hex string used by
/// Platform.SessionSecurity and the 32-byte SHA-256 value in <c>identity.sessions.token_hash</c>
/// (Team 1). Both sides hash the same UTF-8 token with SHA-256, so only the encoding differs.
/// Malformed input returns false and never throws, so a forged or truncated hash is handled
/// as an unknown session (SEC-03).
/// </summary>
internal static class SessionTokenHashConverter
{
    private const int HexLength = SHA256.HashSizeInBytes * 2;

    public static bool TryToBytes(string? hexHash, out byte[] bytes)
    {
        bytes = [];
        if (hexHash is null || hexHash.Length != HexLength)
        {
            return false;
        }

        var buffer = new byte[SHA256.HashSizeInBytes];
        var status = Convert.FromHexString(hexHash, buffer, out _, out var bytesWritten);
        if (status != OperationStatus.Done || bytesWritten != buffer.Length)
        {
            return false;
        }

        bytes = buffer;
        return true;
    }

    public static string ToHex(byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return Convert.ToHexString(hash);
    }
}
