using Platform.Audit.Contracts;

namespace Platform.Api.Modules.Assignments.Audit;

// ASN-04 / ASN-06 TIM 15 (celina 5)

/// <summary>
/// Audit ulaz za Assignments use-case-ove (ASN-04, ASN-06). Pozivalac opisuje ŠTA se desilo;
/// akter, vreme i korelacioni ID dolaze iz pouzdanog serverskog konteksta.
/// Ime nije <c>IAuditWriter</c> jer Platform.Api već ima dva interfejsa tog imena
/// (Identity i Platform.Audit); ovaj je adapter ka zajedničkom <c>ITrustedAuditWriter</c> (tim 12).
/// </summary>
public interface IAssignmentAuditWriter
{
    /// <returns>
    /// <c>true</c> samo kada je zapis sačuvan u Audit modulu. Nikad ne baca izuzetak za očekivane
    /// greške; neuspeh upisa nikad ne daje pristup (fail-closed ostaje na pozivaocu).
    /// </returns>
    ValueTask<bool> WriteAsync(
        AssignmentAuditDraft draft,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pouzdani kontekst trenutnog zahteva: akter iz proverene sesije i korelacioni ID iz middleware-a.
/// Nikad se ne gradi iz tela zahteva, query stringa ili zaglavlja koje šalje klijent.
/// </summary>
public interface IAssignmentAuditContextProvider
{
    AuditContext GetContext();
}
