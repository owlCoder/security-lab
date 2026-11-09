using Microsoft.AspNetCore.Http;
using Team21.Acc.Core.Common;

namespace Team21.Acc.Api.Extensions;

/// <summary>
/// Result to <see cref="IResult"/> for minimal API endpoints. 200 or 204 on success,
/// ProblemDetails on failure.
/// </summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
        {
            return result.Value is null
                ? Results.NoContent()
                : Results.Ok(result.Value);
        }

        return ToProblem(result.Error!);
    }

    public static IResult ToHttpResult(this Result result)
    {
        if (result.IsSuccess)
            return Results.NoContent();

        return ToProblem(result.Error!);
    }

    /// <summary>
    /// 201 with a Location header built from the new value.
    /// </summary>
    public static IResult ToCreatedHttpResult<T>(this Result<T> result, Func<T, string> location)
    {
        if (result.IsSuccess && result.Value is not null)
            return Results.Created(location(result.Value), result.Value);

        return result.IsSuccess
            ? Results.NoContent()
            : ToProblem(result.Error!);
    }

    private static IResult ToProblem(Error error)
    {
        // An unmapped ErrorType becomes a 500 so adding one means updating this switch by hand.
        var status = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.BadRequest => StatusCodes.Status400BadRequest,
            ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["errorType"] = error.Code });
    }
}
