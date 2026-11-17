namespace Platform.Api.Modules.Administration.Api;

/// <summary>Runs after central Access; bounds unknown/chunked bodies before DTO binding too.</summary>
public sealed class AdministrationBodyLimitMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (!AdministrationRequestIdentity.AppliesTo(http) || HttpMethods.IsGet(http.Request.Method))
        { await next(http); return; }

        const int limit = 16 * 1024;
        var original = http.Request.Body;
        await using var bounded = new MemoryStream(limit + 1);
        var chunk = new byte[4096];
        try
        {
            while (true)
            {
                var remaining = limit + 1 - (int)bounded.Length;
                var read = await original.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), http.RequestAborted);
                if (read == 0) break;
                await bounded.WriteAsync(chunk.AsMemory(0, read), http.RequestAborted);
                if (bounded.Length > limit) { http.Response.StatusCode = 413; return; }
            }
        }
        catch (BadHttpRequestException ex)
        { http.Response.StatusCode = ex.StatusCode == 413 ? 413 : 400; return; }
        bounded.Position = 0;
        http.Request.Body = bounded;
        try { await next(http); }
        finally { http.Request.Body = original; }
    }
}
