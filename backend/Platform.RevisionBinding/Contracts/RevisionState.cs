namespace Platform.RevisionBinding.Contracts;

/// <summary>
/// REV-03: Stanje revizije onako kako ga vidi modul za vezivanje pokušaja.
/// </summary>
/// <remarks>
/// Namjerno je ovo zaseban tip, a ne LabRevisionStatus iz modula revizija (REV-05).
/// Tako REV-03 ne zavisi od internog modela drugog tima: adapter koji čita
/// stvarne revizije prevodi njihov status u ovaj. Ako drugi tim doda novo
/// stanje, naš kod se ne mijenja dok adapter to ne mapira svjesno.
/// Stanja prate dijagram 4.5.1 iz specifikacije.
/// </remarks>
public enum RevisionState
{
    /// <summary>Nacrt: autor ga još mijenja. Pokušaj se ne smije vezati za njega.</summary>
    Draft = 0,

    /// <summary>Na pregledu: čeka odobrenje. Pokušaj se ne smije vezati za njega.</summary>
    InReview = 1,

    /// <summary>Objavljena: zaključan snimak sadržaja. Jedino stanje za nove pokušaje.</summary>
    Published = 2,

    /// <summary>Arhivirana: povučena. Ne prihvata nove pokušaje (REV-04), stari ostaju vezani.</summary>
    Archived = 3
}
