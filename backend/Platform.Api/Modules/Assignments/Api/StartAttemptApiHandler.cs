using Platform.Api.Modules.Assignments.Audit;
using Platform.Assignments.Application;
using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.Assignments.Api;

// ASN-04 TIM 15 (celina 5)

/// <summary>
/// Izlaže use-case „započni pokušaj” (član 2, <see cref="StartAttemptService"/>) preko HTTP-a i
/// beleži svaki ishod. <c>StartAttemptService</c> nema audit port, pa se, da se ne menja tuđi kod,
/// zapis piše ovde, posle odluke servisa. Logiku limita i dostupnosti ne ponavljamo.
/// <para>
/// Redosled je namerno razdvojen: (1) use-case odlučuje i eventualno menja stanje, (2) iz ishoda se
/// pravi HTTP odgovor, (3) tek onda ide audit kao best-effort korak van <c>try</c> bloka use-case-a.
/// Pad audita nikad ne menja već donetu odluku: uspešno sačuvan pokušaj ostaje 201, odbijanje ostaje
/// odbijanje, i nema drugog („Failure”) audit zapisa za isti zahtev.
/// </para>
/// </summary>
public sealed class StartAttemptApiHandler(
    StartAttemptService service,
    IAssignmentRepository assignments,
    IAssignmentAuditWriter audit,
    ILogger<StartAttemptApiHandler> logger)
{
    // Poruka iz StartAttemptService (član 2). Test StartAttemptApiHandlerTests je čuva;
    // predlog članu 2: namenski izuzetak umesto poređenja poruke.
    public const string LimitReachedMessage = "Maximum number of attempts reached.";

    public async Task<AssignmentApiResult> HandleAsync(
        string? actorId,
        Guid assignmentId,
        string? requestIdHeader,
        CancellationToken cancellationToken = default)
    {
        if (actorId is null)
        {
            // SessionCheckMiddleware već vraća 401 bez sesije; ovo je slučaj neaktivnog naloga.
            var unauthenticated = new Outcome(
                Error(StatusCodes.Status401Unauthorized, AssignmentErrorCodes.Unauthenticated),
                AssignmentAuditOutcome.Denied, StartAttemptReasonCodes.Unauthenticated);
            await TryAuditAsync(assignmentId, null, unauthenticated);
            return unauthenticated.Result;
        }

        // Idempotentnost (ASN-04): isti ključ ne sme da potroši još jedan pokušaj.
        if (!Guid.TryParse(requestIdHeader, out var requestId) || requestId == Guid.Empty)
        {
            return Error(StatusCodes.Status400BadRequest, AssignmentErrorCodes.InvalidRequestId);
        }

        // (1) + (2): odluka use-case-a i HTTP odgovor. Audit ovde NE učestvuje.
        var outcome = await ExecuteAsync(actorId, assignmentId, requestId, cancellationToken);

        // (3): best-effort audit; ne baca i ne menja outcome.Result.
        await TryAuditAsync(assignmentId, actorId, outcome);

        return outcome.Result;
    }

    private async Task<Outcome> ExecuteAsync(
        string actorId,
        Guid assignmentId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var attemptId = await service.StartAttemptAsync(
                assignmentId, actorId, requestId, cancellationToken);

            return new Outcome(
                new AssignmentApiResult(StatusCodes.Status201Created, new StartAttemptResponse(attemptId)),
                AssignmentAuditOutcome.Success,
                ReasonCode: null,
                attemptId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            // Isti odgovor kao za zabranjen pristup: ID dodele se ne može pogađati.
            return new Outcome(
                Error(StatusCodes.Status404NotFound, AssignmentErrorCodes.AssignmentNotAvailable),
                AssignmentAuditOutcome.Failure, StartAttemptReasonCodes.NotFound);
        }
        catch (UnauthorizedAccessException)
        {
            return new Outcome(
                Error(StatusCodes.Status404NotFound, AssignmentErrorCodes.AssignmentNotAvailable),
                AssignmentAuditOutcome.Denied, StartAttemptReasonCodes.Forbidden);
        }
        catch (InvalidOperationException exception) when (exception.Message == LimitReachedMessage)
        {
            // ASN-04: pokušaj iznad limita se odbija; ništa se ne kreira ni menja.
            return new Outcome(
                Error(StatusCodes.Status409Conflict, AssignmentErrorCodes.AttemptLimitReached),
                AssignmentAuditOutcome.Failure, StartAttemptReasonCodes.AttemptLimitReached);
        }
        catch (InvalidOperationException)
        {
            // Istekla, još nedostupna ili arhivirana dodela (ASN-03).
            return new Outcome(
                Error(StatusCodes.Status409Conflict, AssignmentErrorCodes.AssignmentClosed),
                AssignmentAuditOutcome.Failure, StartAttemptReasonCodes.NotAvailable);
        }
        catch (Exception exception)
        {
            // Skladište ili autorizacija nisu dostupni (izuzetak iz use-case-a, ne iz audita).
            logger.LogError("Start attempt failed with {ExceptionType}.", exception.GetType().Name);
            return new Outcome(
                Error(StatusCodes.Status503ServiceUnavailable, AssignmentErrorCodes.TemporarilyUnavailable),
                AssignmentAuditOutcome.Failure, StartAttemptReasonCodes.Unavailable);
        }
    }

    /// <summary>
    /// Jedan audit zapis po zahtevu, posle odluke. Nikad ne baca: pad čitanja kursa, writer-a ili
    /// Audit modula samo se loguje kao tehnički alarm (bez detalja izuzetka). CancellationToken.None
    /// jer je odluka već doneta i prekinut zahtev ne sme da izgubi zapis.
    /// </summary>
    private async Task TryAuditAsync(Guid assignmentId, string? studentId, Outcome outcome)
    {
        try
        {
            var courseId = await TryGetCourseIdAsync(assignmentId);

            await audit.WriteAsync(
                new AssignmentAuditDraft(
                    AssignmentAuditActions.StartAttempt,
                    outcome.AuditOutcome,
                    assignmentId,
                    courseId,
                    studentId,
                    outcome.ReasonCode,
                    AttemptId: outcome.AttemptId),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(AssignmentAuditWriter.AlarmEvent,
                "Start attempt audit failed with {ExceptionType}; HTTP {StatusCode} is kept.",
                exception.GetType().Name, outcome.Result.StatusCode);
        }
    }

    // Kurs se uzima iz same dodele (pouzdan izvor), nikad od klijenta. Najbolji pokušaj:
    // ako čitanje ne uspe, zapis ide bez kursa.
    private async Task<string?> TryGetCourseIdAsync(Guid assignmentId)
    {
        try
        {
            var assignment = await assignments.GetByIdAsync(assignmentId, CancellationToken.None);
            return assignment?.CourseId;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static AssignmentApiResult Error(int statusCode, string code) =>
        new(statusCode, new AssignmentErrorResponse(code));

    private sealed record Outcome(
        AssignmentApiResult Result,
        AssignmentAuditOutcome AuditOutcome,
        string? ReasonCode,
        Guid? AttemptId = null);
}
