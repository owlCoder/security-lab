using Platform.Access.Domain.Models;

namespace Platform.LabVisibility.Application.EvaluatorKeys;

// KT2 ugovor za upis evaluator ključa (AUT-04).
// Implementira Gajić (kartica 5), kontroler PUT /api/labs/{id}/evaluator-key piše Stojanović (kartica 6),
// a ekran Ferjuc (kartica 8). JSON oblik: docs/contracts/LabVisibility-KT2.md.

// Jedan odgovor iz zahteva. ToString nikad ne ispisuje odgovor (AUT-04, AUD-03).
public sealed record EvaluatorKeyAnswerInput(string TaskId, string ExpectedAnswer)
{
    public override string ToString() =>
        $"EvaluatorKeyAnswerInput {{ TaskId = {TaskId}, ExpectedAnswer = [REDACTED] }}";
}

public sealed record SaveEvaluatorKeyCommand(Guid LabId, IReadOnlyList<EvaluatorKeyAnswerInput> Answers)
{
    public override string ToString() =>
        $"SaveEvaluatorKeyCommand {{ LabId = {LabId}, Answers = [REDACTED x{Answers?.Count ?? 0}] }}";
}

public enum SaveEvaluatorKeyOutcome
{
    // 204, bez tela.
    Saved,

    // 401: zahtev bez identiteta.
    Unauthenticated,

    // 404: odbijeno ili nepostojeće izgledaju isto.
    NotFound,

    // 409: laboratorija je objavljena ili arhivirana, ključ se više ne menja.
    Published,

    // 400: ključ ne prolazi validaciju; telo nosi samo ErrorCode, nikad poslat odgovor.
    Invalid
}

// Kodovi greške za Invalid. Klijent dobija samo kod, nikad tekst odgovora.
public static class SaveEvaluatorKeyErrors
{
    public const string UnknownTask = "key.unknown_task";
    public const string MissingTask = "key.missing_task";
    public const string DuplicateTask = "key.duplicate_task";
    public const string EmptyAnswer = "key.empty_answer";
    public const string AnswerTooLong = "key.answer_too_long";
}

public sealed record SaveEvaluatorKeyResult(SaveEvaluatorKeyOutcome Outcome, string? ErrorCode = null);

public interface ISaveEvaluatorKey
{
    // subject dolazi iz sesije (LabCaller), nikad iz tela zahteva.
    ValueTask<SaveEvaluatorKeyResult> ExecuteAsync(
        SaveEvaluatorKeyCommand command,
        SubjectRef? subject,
        string correlationId,
        CancellationToken cancellationToken = default);
}
