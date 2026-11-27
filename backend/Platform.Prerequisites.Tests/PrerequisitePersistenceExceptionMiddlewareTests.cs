using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Middleware;
using Platform.Prerequisites.Exceptions;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class PrerequisitePersistenceExceptionMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_WhenNoExceptionOccurs_CallsNextMiddleware()
    {
        var nextCalled = false;

        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next);

        await middleware.InvokeAsync(CreateHttpContext());

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task InvokeAsync_WhenPrerequisitePersistenceIsUnavailable_Returns503()
    {
        RequestDelegate next = _ =>
            throw new PrerequisitePersistenceUnavailableException();

        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        Assert.That(
            context.Response.StatusCode,
            Is.EqualTo(StatusCodes.Status503ServiceUnavailable));

        Assert.That(
            context.Response.ContentType,
            Does.StartWith("application/problem+json"));

        Assert.That(
            context.Response.Headers.CacheControl.ToString(),
            Is.EqualTo("no-store"));
    }

    [Test]
    public async Task InvokeAsync_WhenPrerequisitePersistenceIsUnavailable_ReturnsUserFriendlyProblemDetails()
    {
        RequestDelegate next = _ =>
            throw new PrerequisitePersistenceUnavailableException();

        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;

        using var document = await JsonDocument.ParseAsync(context.Response.Body);

        var root = document.RootElement;

        Assert.That(
            root.GetProperty("title").GetString(),
            Is.EqualTo("Laboratorije trenutno nisu dostupne"));

        Assert.That(
            root.GetProperty("detail").GetString(),
            Is.EqualTo("Došlo je do privremenog problema. Pokušajte ponovo kasnije."));

        Assert.That(
            root.GetProperty("status").GetInt32(),
            Is.EqualTo(StatusCodes.Status503ServiceUnavailable));

        Assert.That(
            root.GetRawText(),
            Does.Not.Contain("Prerequisite persistence is unavailable."));
    }

    [Test]
    public void InvokeAsync_WhenAnotherExceptionOccurs_DoesNotHandleIt()
    {
        RequestDelegate next = _ =>
            throw new InvalidOperationException("Unrelated error");

        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(context));
    }

    private static PrerequisitePersistenceExceptionMiddleware CreateMiddleware(
        RequestDelegate next) =>
        new(
            next,
            NullLogger<PrerequisitePersistenceExceptionMiddleware>.Instance);

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();

        context.Response.Body = new MemoryStream();

        return context;
    }
}