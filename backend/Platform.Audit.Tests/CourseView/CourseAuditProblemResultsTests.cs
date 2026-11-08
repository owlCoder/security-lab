using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Audit.Endpoints;

namespace Platform.Audit.Tests.CourseView;

/// <summary>S-14 HTTP error shapes: 400 without values, 503 without data, 401/403 as Access writes them.</summary>
[TestFixture]
public sealed class CourseAuditProblemResultsTests
{
    private const string Correlation = "s14-http-0001";

    private static async Task<(int Status, string ContentType, string CacheControl, JsonElement Body)> Execute(IResult result)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        await using var body = new MemoryStream();
        http.Response.Body = body;

        await result.ExecuteAsync(http);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        return (http.Response.StatusCode, http.Response.ContentType!, http.Response.Headers.CacheControl.ToString(),
            document.RootElement.Clone());
    }

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).ToArray();

    [Test]
    public async Task InvalidFilters_Is400_WithFieldAndCodeOnly()
    {
        var (status, contentType, cache, body) = await Execute(CourseAuditProblemResults.InvalidFilters(
            [new CourseAuditFilterError("pageSize", "OutOfRange"), new CourseAuditFilterError("from", "InvalidFormat")],
            Correlation));

        var errors = body.GetProperty("errors").EnumerateArray().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(contentType, Is.EqualTo("application/problem+json"));
            Assert.That(cache, Is.EqualTo("no-store"));
            Assert.That(body.GetProperty("correlationId").GetString(), Is.EqualTo(Correlation));
            Assert.That(errors, Has.Length.EqualTo(2));
            Assert.That(errors.Select(PropertyNames), Has.All.EquivalentTo(new[] { "field", "code" }));
            Assert.That(errors[0].GetProperty("field").GetString(), Is.EqualTo("pageSize"));
            Assert.That(errors[0].GetProperty("code").GetString(), Is.EqualTo("OutOfRange"));
        });
    }

    [TestCase("<script>alert(1)</script>", "InvalidFormat")]
    [TestCase("from", "2026-11-01T00:00:00Z")]
    [TestCase("type", "x' OR '1'='1")]
    [TestCase("", "Required")]
    public void FilterError_RefusesAnythingButIdentifiers_SoAValueCannotBeEchoed(string field, string code) =>
        Assert.Throws<ArgumentException>(() => _ = new CourseAuditFilterError(field, code));

    [Test]
    public void InvalidFilters_NeedsAtLeastOneError() =>
        Assert.Throws<ArgumentException>(() => CourseAuditProblemResults.InvalidFilters([], Correlation));

    [Test]
    public async Task Unavailable_Is503_WithoutDataOrDetails()
    {
        var (status, contentType, cache, body) = await Execute(CourseAuditProblemResults.Unavailable(Correlation));

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
            Assert.That(contentType, Is.EqualTo("application/problem+json"));
            Assert.That(cache, Is.EqualTo("no-store"));
            Assert.That(PropertyNames(body), Is.EquivalentTo(new[] { "type", "title", "status", "correlationId" }));
        });
    }

    [TestCase(true, StatusCodes.Status401Unauthorized)]
    [TestCase(false, StatusCodes.Status403Forbidden)]
    public async Task AccessDenied_MatchesTheAccessMiddlewareShape(bool unauthenticated, int expected)
    {
        var (status, contentType, cache, body) =
            await Execute(CourseAuditProblemResults.AccessDenied(unauthenticated, Correlation));

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(expected));
            Assert.That(contentType, Is.EqualTo("application/problem+json"));
            Assert.That(cache, Is.EqualTo("no-store"));
            Assert.That(PropertyNames(body), Is.EquivalentTo(new[] { "type", "title", "status", "correlationId" }));
        });
    }
}
