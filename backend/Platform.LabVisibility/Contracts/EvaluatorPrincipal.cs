namespace Platform.LabVisibility.Contracts;

// Servisni subjekat evaluatora. Nije GUID, pa ga korisnik ne može dobiti
// kroz IIdentityContext (korisnički ID je uvek GUID).
public static class EvaluatorPrincipal
{
    public const string Id = "system:evaluator";

    // ID servisnog aktera evaluatora u trajnom auditu (AuditActorKind.Service traži GUID).
    public static readonly Guid ServiceActorId = Guid.Parse("e7a1a7e0-0000-4000-8000-000000000001");
}
