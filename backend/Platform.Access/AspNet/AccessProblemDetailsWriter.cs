using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Platform.Access.AspNet;

public static class AccessProblemDetailsWriter
{
    public static async Task WriteAsync(
        HttpContext context,
        bool unauthenticated,
        string correlationId)
    {
        var statusCode = unauthenticated
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = unauthenticated
                ? "Unauthorized"
                : "Forbidden",
            Type = "about:blank",
        };

        problem.Extensions["correlationId"] = correlationId;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-store";


        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            problem,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            context.RequestAborted);
    }
}