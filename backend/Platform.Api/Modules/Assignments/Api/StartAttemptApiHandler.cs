using Platform.Api.Modules.Assignments.Audit;
using Platform.Assignments.Application;
using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.Assignments.Api;

// ASN-04 TIM 15 (celina 5)

/// <summary>
/// Izlaže use-case „započni pokušaj” (član 2, <see cref="StartAttemptService"/>) preko HTTP-a i
/// beleži svaki ishod. <c>StartAttemptService</c> nema audit port, pa se, da se ne menja tuđi kod,
/// zapis piše ovde, posle odluke servisa. Logiku limita i dostupnosti ne ponavljamo.
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
            await AuditAsync(assignmentId, null, AssignmentAuditOutcome.Denied,
                StartAttemptReasonCodes.Unauthenticated, null);
            return Error(StatusCodes.Status401Unauthorized, AssignmentErrorCodes.Unauthenticated);
        }

        // Idempotentnost (ASN-04): isti ključ ne sme da potroši još jedan pokušaj.
        if (!Guid.TryParse(requestIdHeader, out var requestId) || requestId == Guid.Empty)
        {
            return Error(StatusCodes.Status400BadRequest, AssignmentErrorCodes.InvalidRequestId);
        }

        try
        {
            var attemptId = await service.StartAttemptAsync(
                assignmentId, actorId, requestId, cancellationToken);

            await AuditAsync(assignmentId, actorId, AssignmentAuditOutcome.Success, null, attemptId);
            return new AssignmentApiResult(StatusCodes.Status201Created, new StartAttemptResponse(attemptId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            await AuditAsync(assignmentId, actorId, AssignmentAuditOutcome.Failure,
                StartAttemptReasonCodes.NotFound, null);
            // Isti odgovor kao za zabranjen pristup: ID dodele se ne može pogađati.
            return Error(StatusCodes.Status404NotFound, AssignmentErrorCodes.AssignmentNotAvailable);
        }
        catch (UnauthorizedAccessException)
        {
            await AuditAsync(assignmentId, actorId, AssignmentAuditOutcome.Denied,
                StartAttemptReasonCodes.Forbidden, null);
            return Error(StatusCodes.Status404NotFound, AssignmentErrorCodes.AssignmentNotAvailable);
        }
        catch (InvalidOperationException exception) when (exception.Message == LimitReachedMessage)
        {
            // ASN-04: pokušaj iznad limita se odbija; ništa se ne kreira ni menja.
            await AuditAsync(assignmentId, actorId, AssignmentAuditOutcome.Failure,
                StartAttemptReasonCodes.AttemptLimitReached, null);
            return Error(StatusCodes.Status409Conflict, AssignmentErrorCodes.AttemptLimitReached);
        }
        catch (InvalidOperationException)
        {
            // Istekla, još nedostupna ili arhivirana dodela (ASN-03).
            await AuditAsync(assignmentId, actorId, AssignmentAuditOutcome.Failure,
                StartAttemptReasonCodes.NotAvailable, null);
            return Error(StatusCodes.Status409Conflict, AssignmentErrorCodes.AssignmentClosed);
        }
        catch (Exception exception)
        {
            // Skladište ili autorizacija nisu dostupni. Klijent ne dobija interne detalje.
            logger.LogError("Start attempt failed with {ExceptionType}.", exception.GetType().Name);
            await AuditAsync(assignmentId, actorId, AssignmentAuditOutcome.Failure,
                StartAttemptReasonCodes.Unavailable, null);
            return Error(StatusCodes.Status503ServiceUnavailable, AssignmentErrorCodes.TemporarilyUnavailable);
        }
    }

    // Akcija je već odlučena: CancellationToken.None da prekinut zahtev ne izgubi zapis.
    private async Task AuditAsync(
        Guid assignmentId,
        string? studentId,
        AssignmentAuditOutcome outcome,
        string? reasonCode,
        Guid? attemptId)
    {
        var courseId = await TryGetCourseIdAsync(assignmentId);

        await audit.WriteAsync(
            new AssignmentAuditDraft(
                AssignmentAuditActions.StartAttempt,
                outcome,
                assignmentId,
                courseId,
                studentId,
                reasonCode,
                AttemptId: attemptId),
            CancellationToken.None);
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
}
