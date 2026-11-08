
namespace Platform.Membership.Domain;

/// <summary>
/// Stanje delegacije u datom trenutku. Ne čuva se u bazi, nego se računa iz
/// ExpiresAt i RevokedAt prema serverskom vremenu (OPS-09), pa istek važi bez ikakvog posla u pozadini.
/// </summary>
public enum DelegationState
{
    Active,
    Expired,
    Revoked
}
