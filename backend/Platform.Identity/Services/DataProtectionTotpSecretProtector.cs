using Microsoft.AspNetCore.DataProtection;
using Platform.Identity.Interfaces.Mfa;

namespace Platform.Identity.Services;

/// <summary>ASP.NET Core Data Protection with a purpose that only the TOTP secret uses.</summary>
public sealed class DataProtectionTotpSecretProtector(IDataProtectionProvider provider) : ITotpSecretProtector
{
    // Changing the purpose makes every stored secret unreadable; a new version needs a migration.
    public static readonly string[] Purpose = ["SecurityLab", "Identity", "MfaTotpSecret", "v1"];

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string base32Secret) => _protector.Protect(base32Secret);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
