using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace Platform.Api.Modules.Administration.Api;

/// <summary>
/// Module-specific serialization only. Central Access still decides and audits denials;
/// neither a denied handler nor a failed decision is retried or admitted.
/// </summary>
public sealed class AdministrationResponseMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (!AdministrationRequestIdentity.AppliesTo(http)) { await next(http); return; }
        http.Response.OnStarting(() =>
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Correlation-ID"] = http.TraceIdentifier;
            return Task.CompletedTask;
        });
        if (http.Request.ContentLength > 16 * 1024)
        { http.Response.StatusCode = 413; return; }
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            size.MaxRequestBodySize = 16 * 1024;

        // These four endpoints return bounded JSON DTOs, never streamed files or content.
        var original = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            await next(http);
            if (http.Response.StatusCode is 401 or 403)
            {
                http.Response.StatusCode = http.Items.ContainsKey(AdministrationRequestIdentity.IdentityUnavailableKey)
                    ? 503 : http.Response.StatusCode == 401 ? 401 : 404;
                http.Response.ContentType = "application/json";
                http.Response.ContentLength = null;
                var code = http.Response.StatusCode switch
                { 401 => "AUTHENTICATION_REQUIRED", 503 => "DEPENDENCY_UNAVAILABLE", _ => "RESOURCE_UNAVAILABLE" };
                await JsonSerializer.SerializeAsync(original, new { code }, cancellationToken: http.RequestAborted);
            }
            else
            {
                buffer.Position = 0;
                await buffer.CopyToAsync(original, http.RequestAborted);
            }
        }
        finally { http.Response.Body = original; }
    }
}
