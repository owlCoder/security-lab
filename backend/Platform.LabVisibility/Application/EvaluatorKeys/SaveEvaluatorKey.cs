using Platform.Access.Domain.Models;

namespace Platform.LabVisibility.Application.EvaluatorKeys;

// AUT-04: upis evaluator ključa za nacrt.
// KT2 ugovor: do kartice 5 (Gajić) svaki zahtev se odbija kao NotFound, pa kontroler i ekran mogu da se
// spoje ranije, a da niko ništa ne upiše. Gajić dodaje: odluku (lab.key.write) → proveru stanja →
// validaciju → IEvaluatorKeyStore.SaveAsync → audit lab.key.write bez sadržaja.
public sealed class SaveEvaluatorKey : ISaveEvaluatorKey
{
    public ValueTask<SaveEvaluatorKeyResult> ExecuteAsync(
        SaveEvaluatorKeyCommand command,
        SubjectRef? subject,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new SaveEvaluatorKeyResult(SaveEvaluatorKeyOutcome.NotFound));
}
