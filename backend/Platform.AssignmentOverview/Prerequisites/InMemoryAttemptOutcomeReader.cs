using Platform.AssignmentPrerequisites.Contracts;
 
namespace Platform.AssignmentOverview.Prerequisites;
 
// ASN-07 TIM 16
// Privremena zamena za modul pokusaja/evaluacije (ATT/EVAL), koji jos ne postoji.
public sealed class InMemoryAttemptOutcomeReader : IAttemptOutcomeReader
{
    private readonly List<AttemptOutcomeSnapshot> _attempts = [];
 
    public void Add(AttemptOutcomeSnapshot attempt) => _attempts.Add(attempt);
 
    // Skraceno: student je zavrsio zadatak sa ishodom "uspesan".
    public void AddPassed(string studentId, Guid assignmentId) =>
        Add(new AttemptOutcomeSnapshot(
            Guid.NewGuid(),
            assignmentId,
            studentId,
            AttemptState.Completed,
            EvaluationStatus.Completed,
            EvaluationOutcome.Passed));
 
    public ValueTask<IReadOnlyList<AttemptOutcomeSnapshot>> GetForStudentAsync(
        string studentId,
        IReadOnlyCollection<Guid> assignmentIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AttemptOutcomeSnapshot> result = _attempts
            .Where(a => a.StudentId == studentId && assignmentIds.Contains(a.AssignmentId))
            .ToList();
 
        return ValueTask.FromResult(result);
    }
}

// Objasnjenje klase:
// InMemoryAttemptOutcomeReader je implementacija IAttemptOutcomeReader koja cuva ishode
// pokusaja studenata u memoriji (lista _attempts), umesto u bazi ili u pravom modulu
// pokusaja/evaluacije (ATT/EVAL), koji jos ne postoji. Koristi se za proveru preduslova
// zadatka: servis za preduslove preko GetForStudentAsync dobija ishode pokusaja datog
// studenta za trazene zadatke i na osnovu njih odlucuje da li je preduslov ispunjen.
// - Add dodaje proizvoljan snimak ishoda pokusaja.
// - AddPassed je skracenica za dodavanje zavrsenog pokusaja sa ishodom "uspesan".
// - GetForStudentAsync filtrira pokusaje po studentu i listi zadataka.
// Klasa nije thread-safe i podaci se gube pri restartu, pa je namenjena samo za razvoj i
// testove, dok se ne uvede prava implementacija.