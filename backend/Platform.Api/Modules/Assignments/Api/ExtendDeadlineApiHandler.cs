using System.Globalization;
using System.Text.RegularExpressions;
using Platform.Assignments.Application;

namespace Platform.Api.Modules.Assignments.Api;

// ASN-06 TIM 15 (celina 5)

/// <summary>
/// Izlaže use-case „produži rok studentu” (član 3, <see cref="ExtendDeadlineService"/>) preko HTTP-a.
/// Servis sam proverava pravo (port člana 4) i sam beleži svaki ishod kroz
/// <c>IDeadlineExtensionAuditWriter</c> (naš <c>DeadlineExtensionAuditAdapter</c>), pa ovde nema
/// dodatnog audit zapisa da ne bi bilo duplikata. Handler samo daje pouzdanog aktera i mapira izuzetke.
/// </summary>
public sealed partial class ExtendDeadlineApiHandler(
    ExtendDeadlineService service,
    ILogger<ExtendDeadlineApiHandler> logger)
{
    private const int MaxTimestampLength = 40;

    // Zona je obavezna: "2026-11-20T23:59:00Z" ili "2026-11-20T23:59:00+01:00".
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    public async Task<AssignmentApiResult> HandleAsync(
        string? actorId,
        Guid assignmentId,
        string studentId,
        ExtendDeadlineRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (actorId is null)
        {
            return Error(StatusCodes.Status401Unauthorized, AssignmentErrorCodes.Unauthenticated);
        }

        if (!TryParseDeadline(request?.NewDueAtUtc, out var newDueAtUtc))
        {
            return Error(StatusCodes.Status400BadRequest, AssignmentErrorCodes.InvalidDeadline);
        }

        try
        {
            var result = await service.ExtendDeadlineAsync(
                actorId, assignmentId, studentId, newDueAtUtc, cancellationToken);

            return new AssignmentApiResult(
                StatusCodes.Status200OK,
                new ExtendDeadlineResponse(
                    result.AssignmentId,
                    result.StudentId,
                    result.DueAtUtc,
                    result.PreviousDueAtUtc,
                    result.ExtendedAtUtc));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            // Isti odgovor za nepostojeću dodelu i za zabranu (servis ih ne razlikuje namerno).
            return Error(StatusCodes.Status403Forbidden, AssignmentErrorCodes.Forbidden);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Error(StatusCodes.Status400BadRequest, AssignmentErrorCodes.InvalidDeadline);
        }
        catch (ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, AssignmentErrorCodes.InvalidStudent);
        }
        catch (InvalidOperationException)
        {
            // Arhivirana dodela, student nije član kursa ili istovremena izmena.
            return Error(StatusCodes.Status409Conflict, AssignmentErrorCodes.DeadlineExtensionRejected);
        }
        catch (Exception exception)
        {
            logger.LogError("Deadline extension failed with {ExceptionType}.", exception.GetType().Name);
            return Error(StatusCodes.Status503ServiceUnavailable, AssignmentErrorCodes.TemporarilyUnavailable);
        }
    }

    public static bool TryParseDeadline(string? value, out DateTimeOffset result)
    {
        result = default;

        if (string.IsNullOrEmpty(value)
            || value.Length > MaxTimestampLength
            || !TimestampPattern().IsMatch(value))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        result = parsed.ToUniversalTime();
        return true;
    }

    private static AssignmentApiResult Error(int statusCode, string code) =>
        new(statusCode, new AssignmentErrorResponse(code));
}
