namespace Platform.AssignmentOverview.Infrastructure;

// ASN-02/ASN-07 TIM 16
// Modul drugog tima još nije povezan van Developmenta.
// Poseban tip greške, da bi endpoint za tačno ovaj slučaj vratio kontrolisan odgovor 503,
// a da ostale, neočekivane greške ne sakrije.
public sealed class DependencyUnavailableException : InvalidOperationException
{
    public DependencyUnavailableException(string contract)
        : base($"{contract} has no implementation outside Development yet.")
    {
        Contract = contract;
    }

    public string Contract { get; }
}