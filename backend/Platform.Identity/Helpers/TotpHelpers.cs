using OtpNet;

namespace Platform.Identity.Helpers;

public static class TotpHelpers
{
    private const string Issuer = "Security Lab";
    private const int Period = 30;
    private const int Digits = 6;

    public static string GenerateSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(key);
        return base32Secret;
    }

    public static (bool success, long timeStepMatched) Verify(string userCode, string base32Secret)
    {
        if (string.IsNullOrWhiteSpace(userCode)
            || userCode.Length != Digits
            || userCode.Any(character => character is < '0' or > '9'))
            return (false, 0);

        var window = new VerificationWindow(future: 1, previous: 1);

        try
        {
            var secretKey = Base32Encoding.ToBytes(base32Secret);
            var totp = new Totp(secretKey);
            var success = totp.VerifyTotp(userCode, out long timeStepMatched, window);
            return (success, timeStepMatched);
        }
        catch (ArgumentException)
        {
            return (false, 0);
        }
    }

    public static string GenerateTotpUri(string email, string base32Secret)
    {
        var uri = new OtpUri(
            schema: OtpType.Totp,
            secret: base32Secret,
            user: email,
            issuer: Issuer,
            algorithm: OtpHashMode.Sha1,
            digits: Digits,
            period: Period
        );

        // Result: otpauth://totp/{issuer}:{email}?secret={base32}&issuer={issuer}&algorithm=SHA1&digits=6&period=30
        return uri.ToString();
    }
}