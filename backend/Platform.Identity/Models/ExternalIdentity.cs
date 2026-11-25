namespace Platform.Identity.Models;

/// <summary>
/// IDN-08: claims taken from an id_token that the OIDC handler has already validated
/// (signature, issuer, audience, expiry, nonce). Never built from request headers, body or query.
/// </summary>
public sealed record ExternalIdentity(string Issuer, string Subject, string? Email, bool EmailVerified)
{
    // The e-mail and subject are personal data, so they never go into a log via ToString.
    public override string ToString() => $"ExternalIdentity {{ Issuer = {Issuer}, Subject = ***, Email = *** }}";
}