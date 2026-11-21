namespace Platform.Competencies.Domain;

public static class BuiltInCompetencies
{
    // Persisted IDs: keep existing values when changing names or adding catalog entries.
    public static readonly Guid AccessControlId = Guid.Parse("4b677e63-204f-4688-a0eb-a339c343f101");
    public static readonly Guid LogAnalysisId = Guid.Parse("4b677e63-204f-4688-a0eb-a339c343f102");
    public static readonly Guid SecureConfigurationId = Guid.Parse("4b677e63-204f-4688-a0eb-a339c343f103");

    public static IReadOnlyList<Competency> All { get; } = Array.AsReadOnly(new[]
    {
        new Competency(AccessControlId, "Kontrola pristupa", "Prepoznavanje i primena pravila pristupa resursima."),
        new Competency(LogAnalysisId, "Analiza bezbednosnih logova", "Tumačenje događaja i prepoznavanje bezbednosnih nepravilnosti."),
        new Competency(SecureConfigurationId, "Bezbedna konfiguracija", "Prepoznavanje i otklanjanje nebezbednih podešavanja."),
    });
}
