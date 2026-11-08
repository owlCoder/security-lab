namespace Platform.AssignmentOverview.Prerequisites;
 
// ASN-07 TIM 16
// Dodela cuva PublishedLabRevisionId (string), a preduslovi su vezani za laboratoriju (Guid).
// Ovaj ugovor odgovara na jedno pitanje: kojoj laboratoriji pripada ta objavljena revizija?
// Vraca null kad revizija nije poznata. Pozivalac tada odbija (podrazumevano odbijanje).
// Prava implementacija (baza) se dogovara kasnije; za KT1 postoji InMemoryRevisionLaboratoryResolver.
public interface IRevisionLaboratoryResolver
{
    ValueTask<Guid?> ResolveLaboratoryIdAsync(
        string publishedLabRevisionId,
        CancellationToken cancellationToken = default);
}