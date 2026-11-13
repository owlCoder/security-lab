using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;

namespace Platform.Audit.Endpoints;

/// <summary>
/// One refused filter in a 400 response: the filter name and a stable code, never the value.
/// S-07 supplies them as <c>CourseAuditQueryField</c> and <c>CourseAuditQueryErrorCode</c>.
/// </summary>
internal sealed partial record CourseAuditFilterError
{
    public CourseAuditFilterError(string field, string code)
    {
        // Only identifier-like names: a rejected input value can never be passed through here.
        Field = NamePattern().IsMatch(field ?? string.Empty)
            ? field!
            : throw new ArgumentException("A filter name must be an identifier.", nameof(field));
        Code = NamePattern().IsMatch(code ?? string.Empty)
            ? code!
            : throw new ArgumentException("An error code must be an identifier.", nameof(code));
    }

    public string Field { get; }

    public string Code { get; }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}

/// <summary>
/// Error responses of the course audit endpoint (AUD-06, S-14). Every response is problem+json with
/// <c>Cache-Control: no-store</c> and the request correlation ID, and carries no event data, no
/// course existence hint and no exception text. 401/403 reuse the Access writer, so a denial from
/// the handler looks exactly like a denial from AccessControlMiddleware (same body for K and L).
/// </summary>
internal static class CourseAuditProblemResults
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IResult AccessDenied(bool unauthenticated, string correlationId) =>
        new AccessDeniedResult(unauthenticated, correlationId);

    public static IResult InvalidFilters(IEnumerable<CourseAuditFilterError> errors, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var list = errors.Select(error => new { field = error.Field, code = error.Code }).ToArray();
        if (list.Length == 0)
            throw new ArgumentException("A 400 response needs at least one refused filter.", nameof(errors));

        return new ProblemResult(StatusCodes.Status400BadRequest, "Bad Request", correlationId, list);
    }

    public static IResult Unavailable(string correlationId) =>
        new ProblemResult(StatusCodes.Status503ServiceUnavailable, "Service Unavailable", correlationId, null);

    private sealed class AccessDeniedResult(bool unauthenticated, string correlationId) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) =>
            AccessProblemDetailsWriter.WriteAsync(httpContext, unauthenticated, correlationId);
    }

    private sealed class ProblemResult(int status, string title, string correlationId, object? errors) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var problem = new ProblemDetails { Status = status, Title = title, Type = "about:blank" };
            problem.Extensions["correlationId"] = correlationId;
            if (errors is not null)
                problem.Extensions["errors"] = errors;

            httpContext.Response.Clear();
            httpContext.Response.StatusCode = status;
            httpContext.Response.ContentType = "application/problem+json";
            httpContext.Response.Headers.CacheControl = "no-store";

            await JsonSerializer.SerializeAsync(
                httpContext.Response.Body, problem, Json, httpContext.RequestAborted);
        }
    }
}
