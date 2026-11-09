using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;

namespace Platform.Api.Modules.Assignments.Api;

// ASN-04 / ASN-06 TIM 15 (celina 5)
//
// POST /api/assignments/{assignmentId}/attempts                                  (ASN-04)
//   Zaglavlje Idempotency-Key: <guid>. 201 { attemptId } | 400 | 401 | 404 | 409 | 503
// POST /api/assignments/{assignmentId}/students/{studentId}/deadline-extensions  (ASN-06)
//   Telo { "newDueAtUtc": "2026-11-20T23:59:00Z" }. 200 | 400 | 401 | 403 | 409 | 503
//
// Identitet: SessionCheckMiddleware (IDN-02) odbija zahtev bez sesije sa 401 pre kontrolera.
// ACC-05: [DelegatedAuthorization] znači da odluku donosi use-case kroz svoje portove
// (IStartAttemptAuthorization / IDeadlineExtensionAuthorization, član 4), PRE čitanja ili izmene
// podataka; isti obrazac kao LabPreviewController (AUT-07).
[DelegatedAuthorization]
[ApiController]
[Route("api/assignments")]
public sealed class AssignmentsController(
    AssignmentCaller caller,
    StartAttemptApiHandler startAttempt,
    ExtendDeadlineApiHandler extendDeadline) : ControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    [HttpPost("{assignmentId:guid}/attempts")]
    public async Task<IActionResult> StartAttempt(
        Guid assignmentId,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        NoStore();
        var actorId = await caller.GetActorIdAsync(cancellationToken);
        var result = await startAttempt.HandleAsync(actorId, assignmentId, idempotencyKey, cancellationToken);
        return StatusCode(result.StatusCode, result.Body);
    }

    [HttpPost("{assignmentId:guid}/students/{studentId}/deadline-extensions")]
    public async Task<IActionResult> ExtendDeadline(
        Guid assignmentId,
        string studentId,
        [FromBody] ExtendDeadlineRequest? request,
        CancellationToken cancellationToken)
    {
        NoStore();
        var actorId = await caller.GetActorIdAsync(cancellationToken);
        var result = await extendDeadline.HandleAsync(actorId, assignmentId, studentId, request, cancellationToken);
        return StatusCode(result.StatusCode, result.Body);
    }

    // Odgovori sadrže lične rokove i ID-jeve pokušaja; ne smeju u keš browsera ili proxy-ja.
    private void NoStore() => Response.Headers.CacheControl = "no-store";
}
