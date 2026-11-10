namespace Platform.RevisionBinding.Domain;

/// <summary>
/// REV-03: Rezultat pokušaja vezivanja: ili uspjeh sa vezom, ili odbijanje sa razlogom.
/// </summary>
/// <remarks>
/// Isti obrazac koriste i drugi moduli u repou (npr. LabRevision.CreationResult):
/// očekivani neuspjeh se vraća kao vrijednost, bez bacanja izuzetaka i bez null-a.
/// Privatni konstruktor znači da postoje SAMO ova dva ishoda.
/// </remarks>
public abstract record BindingResult
{
    private BindingResult() { }

    /// <summary>Kod odluke, isti za uspjeh i odbijanje (za audit i testove).</summary>
    public abstract BindingOutcome Outcome { get; }

    /// <summary>Vezivanje je uspjelo.</summary>
    public sealed record Bound(AttemptRevisionBinding Binding) : BindingResult
    {
        public override BindingOutcome Outcome => BindingOutcome.Bound;
    }

    /// <summary>Vezivanje je odbijeno; razlog je u Reason.</summary>
    public sealed record Rejected(BindingOutcome Reason) : BindingResult
    {
        public override BindingOutcome Outcome => Reason;
    }
}
