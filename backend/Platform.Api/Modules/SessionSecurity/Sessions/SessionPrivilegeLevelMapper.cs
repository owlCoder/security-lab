using Platform.Identity.Enums;
using Platform.SessionSecurity.Domain;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Sessions;

/// <summary>
/// IDN-11 (D3): maps session levels between Platform.Identity and Platform.SessionSecurity.
/// Identity has no level above <see cref="AuthLevel.Active"/>, so <see cref="SessionPrivilegeLevel.Elevated"/>
/// is stored as Active (documented limitation). A rotation never stores a pre-authentication session.
/// </summary>
internal static class SessionPrivilegeLevelMapper
{
    // Mapped by name, never by numeric cast (same rule as StoreSessionCreator.ToStoredLevel): a cast
    // would store Elevated (2) as a value Identity does not define.
    public static SessionPrivilegeLevel ToCore(AuthLevel level) => level switch
    {
        AuthLevel.MfaPending => SessionPrivilegeLevel.PreAuthentication,
        AuthLevel.Active => SessionPrivilegeLevel.Authenticated,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown Identity session level."),
    };

    // PreAuthentication is a programming error: SessionRotationService rejects it before the store.
    public static AuthLevel ToIdentity(SessionPrivilegeLevel level) => level switch
    {
        SessionPrivilegeLevel.Authenticated or SessionPrivilegeLevel.Elevated => AuthLevel.Active,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "A rotated session needs an authenticated level."),
    };
}
