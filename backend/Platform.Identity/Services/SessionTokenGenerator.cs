using System.Security.Cryptography;
using System.Text;

namespace Platform.Identity.Services;

internal static class SessionTokenGenerator
{
    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32); // 256 bita, kriptografski siguran generator
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] Hash(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));
}