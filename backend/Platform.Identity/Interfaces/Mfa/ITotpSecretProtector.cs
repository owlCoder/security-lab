namespace Platform.Identity.Interfaces.Mfa;

/// <summary>
/// IDN-06: encrypts the TOTP secret at rest. Its own type, so that no other module gets the MFA purpose by
/// injecting a plain <c>IDataProtector</c>; other modules create their own protector from
/// <c>IDataProtectionProvider</c> with their own purpose.
/// </summary>
public interface ITotpSecretProtector
{
    string Protect(string base32Secret);

    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The ciphertext was not made with this purpose or the key ring no longer has its key.
    /// </exception>
    string Unprotect(string ciphertext);
}
