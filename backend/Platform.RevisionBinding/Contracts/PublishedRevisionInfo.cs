namespace Platform.RevisionBinding.Contracts;

/// <summary>
/// REV-03: Minimalan skup podataka o reviziji koji je potreban da se odluči
/// da li se pokušaj smije vezati za nju.
/// </summary>
/// <remarks>
/// Sadrži samo metapodatke, nikad sam sadržaj zadatka ni ključ evaluatora.
/// Tako odluka o vezivanju ne može slučajno da iznese skrivene kriterijume.
/// </remarks>
/// <param name="RevisionId">Stabilan identifikator revizije (REV-16).</param>
/// <param name="LaboratoryId">Laboratorija kojoj revizija pripada.</param>
/// <param name="CourseId">Kurs u kojem je revizija nastala; mora se poklopiti sa kursom dodjele.</param>
/// <param name="State">Trenutno stanje revizije.</param>
/// <param name="Number">Redni broj objave unutar laboratorije; null dok revizija nije objavljena.</param>
/// <param name="ContentHash">SHA-256 snimka sadržaja (64 heksadecimalna znaka, mala slova).</param>
public sealed record PublishedRevisionInfo(
    Guid RevisionId,
    Guid LaboratoryId,
    Guid CourseId,
    RevisionState State,
    int? Number,
    string ContentHash);
