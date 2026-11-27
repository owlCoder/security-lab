using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;

namespace Platform.Identity.Testing;

/// <summary>US-15: test clients that send the CSRF token like the frontend does.</summary>
public static class CsrfClientExtensions
{
    /// <summary>
    /// Same client as <c>factory.CreateClient(options)</c> (same base address, redirects and cookie handling),
    /// with <see cref="CsrfTokenHandler"/> added closest to the server.
    /// </summary>
    public static HttpClient CreateCsrfClient<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory, WebApplicationFactoryClientOptions? options = null)
        where TEntryPoint : class
    {
        options ??= new WebApplicationFactoryClientOptions();

        var handlers = new List<DelegatingHandler>();
        if (options.AllowAutoRedirect)
            handlers.Add(new RedirectHandler(options.MaxAutomaticRedirections));
        if (options.HandleCookies)
            handlers.Add(new CookieContainerHandler());
        handlers.Add(new CsrfTokenHandler());

        return factory.CreateDefaultClient(options.BaseAddress, handlers.ToArray());
    }
}
