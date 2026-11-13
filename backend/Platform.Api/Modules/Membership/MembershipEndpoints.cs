//using Platform.Api.Security;
using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.Membership.Application;
using Platform.Audit.Contracts;
namespace Platform.Api.Modules.Membership;
/// <summary>
/// Telo zahteva za upis. Namerno sadrži samo nalog koji se upisuje: ko upisuje
/// određuje server (IRequestSubjectResolver), a ne klijent.
/// </summary>
public sealed record EnrollStudentRequest(Guid? AccountId);
public static class MembershipEndpoints
{
    public static IEndpointRouteBuilder MapMembershipEndpoints(this IEndpointRouteBuilder app)
    {
        // ACC-02: upis studenta u kurs.
        // Pravo proverava Access middleware (ugovor Tima 3, ACC-05) pre ulaska u handler;
        // servis proveru ponavlja (spec. 2.1).
        app.MapPost("/api/courses/{courseId:guid}/members", EnrollStudentAsync)
            .RequireAccess(MembershipActions.EnrollMember, MembershipActions.CourseResourceType, "courseId");
        return app;
    }
    private static async Task<IResult> EnrollStudentAsync(
    Guid courseId,
    EnrollStudentRequest? body,
    HttpContext context,
    IRequestSubjectResolver subjectResolver,
    IMembershipService memberships,
     ICorrelationContext correlation,
    CancellationToken cancellationToken)
    {
        // AUD-04: isti ID kao u zaglavlju X-Correlation-ID, da se zahtev prati kroz module.
        var correlationId = correlation.CorrelationId ?? context.TraceIdentifier;
        var result = await memberships.EnrollStudentAsync(
        subjectResolver.Resolve(context),
        courseId,
        body?.AccountId ?? Guid.Empty,
        correlationId,
        cancellationToken);
        // Poruke ne sadrže ID kursa ni detalje iz baze. Nepostojeći i neaktivan nalog
        // daju isti odgovor, da se preko upisa ne bi saznavao status tuđih naloga.
        return result.Outcome switch
        {
            EnrollmentOutcome.Enrolled when result.Member is { } member => Results.Created(
            $"/api/courses/{member.CourseId}/members/{member.AccountId}",
            new
            {
                id = member.Id,
                courseId = member.CourseId,
                accountId = member.AccountId,
                role = member.Role.ToString(),
                status = member.Status.ToString(),
                enrolledAt = member.EnrolledAt,
                correlationId
            }),
            EnrollmentOutcome.Unauthenticated =>
            Error(StatusCodes.Status401Unauthorized, "unauthenticated", correlationId),
            EnrollmentOutcome.Forbidden =>
            Error(StatusCodes.Status403Forbidden, "forbidden", correlationId),
            EnrollmentOutcome.InvalidInput => Results.BadRequest(new
            {
                error = "invalid_input",
                errors = new[] { "Polje accountId je obavezno." },
                correlationId
            }),
            EnrollmentOutcome.CourseNotFound =>
            Error(StatusCodes.Status404NotFound, "course_not_found", correlationId),
            EnrollmentOutcome.CourseArchived =>
            Error(StatusCodes.Status409Conflict, "course_archived", correlationId),
            EnrollmentOutcome.AccountNotFound or EnrollmentOutcome.AccountNotActive =>
            Error(StatusCodes.Status422UnprocessableEntity, "account_not_eligible", correlationId),
            EnrollmentOutcome.AlreadyEnrolled =>
            Error(StatusCodes.Status409Conflict, "already_enrolled", correlationId),
            _ =>
            Error(StatusCodes.Status500InternalServerError, "unexpected", correlationId)
        };
    }
    private static IResult Error(int statusCode, string error, string correlationId) =>
    Results.Json(new { error, correlationId }, statusCode: statusCode);
}