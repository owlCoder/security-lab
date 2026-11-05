using System.Security.Cryptography;

namespace Platform.Laboratories.Domain.Rules;

public static class RevisionContentHash
{
    public const string Algorithm = "SHA-256";
    public const int HexLength = 64;

    public static ComputationResult Compute(ReadOnlySpan<byte> snapshot)
    {
        if (snapshot.IsEmpty)
            return new ComputationResult.Failure(ComputationError.EmptySnapshot);

        var hash = Convert.ToHexString(SHA256.HashData(snapshot)).ToLowerInvariant();
        return new ComputationResult.Success(hash);
    }

    public static bool IsValid(string hash)
    {
        if (string.IsNullOrEmpty(hash) || hash.Length != HexLength)
            return false;

        foreach (var character in hash)
        {
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    public enum ComputationError
    {
        EmptySnapshot = 1
    }

    public abstract record ComputationResult
    {
        private ComputationResult() { }

        public sealed record Success(string Value) : ComputationResult;
        public sealed record Failure(ComputationError Error) : ComputationResult;
    }
}
