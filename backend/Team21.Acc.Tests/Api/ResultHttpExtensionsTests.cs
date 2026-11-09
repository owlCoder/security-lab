using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Team21.Acc.Api.Extensions;
using Team21.Acc.Core.Common;

namespace Team21.Acc.Tests.Api;

public class ResultHttpExtensionsTests
{
    private sealed record Dto(Guid Id);

    [Test]
    public void SuccessWithValueIs200WithTheValue()
    {
        var dto = new Dto(Guid.NewGuid());
        Result<Dto> result = dto;

        var http = result.ToHttpResult();

        Assert.That(http, Is.TypeOf<Ok<Dto>>());
        Assert.That(((Ok<Dto>)http).Value, Is.EqualTo(dto));
    }

    [Test]
    public void SuccessWithoutValueIs204()
    {
        Assert.That(Result.Success().ToHttpResult(), Is.TypeOf<NoContent>());
    }

    [Test]
    public void CreatedPointsAtTheNewValue()
    {
        var dto = new Dto(Guid.NewGuid());
        Result<Dto> result = dto;

        var http = result.ToCreatedHttpResult(d => $"/api/things/{d.Id}");

        Assert.That(http, Is.TypeOf<Created<Dto>>());
        Assert.That(((Created<Dto>)http).Location, Is.EqualTo($"/api/things/{dto.Id}"));
    }

    [TestCase(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [TestCase(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [TestCase(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [TestCase(ErrorType.BadRequest, StatusCodes.Status400BadRequest)]
    [TestCase(ErrorType.Validation, StatusCodes.Status422UnprocessableEntity)]
    [TestCase(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [TestCase(ErrorType.Internal, StatusCodes.Status500InternalServerError)]
    public void EachErrorTypeHasItsStatus(ErrorType type, int status)
    {
        Result<Dto> result = new Error(type, "message");

        var problem = (ProblemHttpResult)result.ToHttpResult();

        Assert.That(problem.StatusCode, Is.EqualTo(status));
        Assert.That(problem.ProblemDetails.Detail, Is.EqualTo("message"));
        Assert.That(problem.ProblemDetails.Extensions["errorType"], Is.EqualTo(type.ToString()));
    }

    [Test]
    public void UnknownErrorTypeIs500()
    {
        Result result = new Error((ErrorType)999, "message");

        var problem = (ProblemHttpResult)result.ToHttpResult();

        Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
    }
}
