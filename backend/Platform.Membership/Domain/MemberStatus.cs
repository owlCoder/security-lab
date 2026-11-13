
namespace Platform.Membership.Domain;

/// <summary>
/// Status članstva. Vrednosti su iste kao MembershipStatus u Platform.Access (Tim 3).
/// Ispisan član (Left) ostaje u bazi, jer istupanje ne sme da menja istoriju (spec. 2.1).
/// </summary>
public enum MemberStatus
{
    Active,
    Left,
    Suspended
}
