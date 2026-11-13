namespace Platform.Identity.Interfaces;

public interface IBackupCodeGenerator
{
    string GenerateCode();

    bool TryComputeHash(string code, out string codeHash);

    bool VerifyHash(string code, string storedHash);
}
