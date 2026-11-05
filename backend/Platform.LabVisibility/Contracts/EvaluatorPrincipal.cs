namespace Platform.LabVisibility.Contracts;

// Servisni subjekat evaluatora. Nije GUID, pa ga korisnik ne može dobiti
// kroz IIdentityContext (korisnički ID je uvek GUID).
public static class EvaluatorPrincipal
{
    public const string Id = "system:evaluator";
}
