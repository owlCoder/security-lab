using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.ExceptionHandling;
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

        var middleware = new PrerequisitePersistenceExceptionMiddleware(
            next,
            CreateHandler());

        await middleware.InvokeAsync(CreateHttpContext());

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task InvokeAsync_WhenPrerequisitePersistenceIsUnavailable_Returns503()
    {
        var handler = CreateHandler();

        RequestDelegate next = _ =>
            throw new PrerequisitePersistenceUnavailableException();

        var middleware = new PrerequisitePersistenceExceptionMiddleware(
            next,
            handler);

        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        Assert.That(
            context.Response.StatusCode,
            Is.EqualTo(StatusCodes.Status503ServiceUnavailable));

        Assert.That(
            context.Response.ContentType,
            Does.StartWith("application/problem+json"));
    }

    [Test]
    public void InvokeAsync_WhenAnotherExceptionOccurs_DoesNotHandleIt()
    {
        var handler = CreateHandler();

        RequestDelegate next = _ =>
            throw new InvalidOperationException("Unrelated error");

        var middleware = new PrerequisitePersistenceExceptionMiddleware(
            next,
            handler);

        var context = CreateHttpContext();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(context));
    }

    private static PrerequisitePersistenceExceptionHandler CreateHandler() =>
        new(NullLogger<PrerequisitePersistenceExceptionHandler>.Instance);

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();

        context.Response.Body = new MemoryStream();

        return context;
    }
}