using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.ExceptionHandling;
using Platform.Prerequisites.Exceptions;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class PrerequisitePersistenceExceptionHandlerTests
{
    private readonly PrerequisitePersistenceExceptionHandler _handler =
        new(NullLogger<PrerequisitePersistenceExceptionHandler>.Instance);

    [Test]
    public async Task TryHandleAsync_WhenPrerequisitePersistenceIsUnavailable_Returns503()
    {
        var context = CreateHttpContext();

        var handled = await _handler.TryHandleAsync(
            context,
            new PrerequisitePersistenceUnavailableException(),
            CancellationToken.None);

        Assert.That(handled, Is.True);
        Assert.That(
            context.Response.StatusCode,
            Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
    }

    [Test]
    public async Task TryHandleAsync_WhenPrerequisitePersistenceIsUnavailable_ReturnsUserFriendlyProblemDetails()
    {
        var context = CreateHttpContext();

        await _handler.TryHandleAsync(
            context,
            new PrerequisitePersistenceUnavailableException(),
            CancellationToken.None);

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
            root.GetRawText(),
            Does.Not.Contain("Prerequisite persistence is unavailable."));

        Assert.That(
            root.GetProperty("status").GetInt32(),
            Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
    }

    [Test]
    public async Task TryHandleAsync_WhenExceptionIsUnrelated_ReturnsFalse()
    {
        var context = CreateHttpContext();

        var handled = await _handler.TryHandleAsync(
            context,
            new InvalidOperationException("Unrelated error"),
            CancellationToken.None);

        Assert.That(handled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(context.Response.Body.Length, Is.Zero);
    }

    [Test]
    public async Task TryHandleAsync_WhenPrerequisitePersistenceIsUnavailable_SetsProblemJsonContentType()
    {
        var context = CreateHttpContext();

        await _handler.TryHandleAsync(
            context,
            new PrerequisitePersistenceUnavailableException(),
            CancellationToken.None);

        Assert.That(
            context.Response.ContentType,
            Does.StartWith("application/problem+json"));
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = "/api/laboratories/test/prerequisites"
            }
        };

        context.Response.Body = new MemoryStream();

        return context;
    }
}