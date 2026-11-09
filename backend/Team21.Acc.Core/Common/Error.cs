namespace Team21.Acc.Core.Common;

/// <summary>
/// A failure and the message the client gets. The message goes out as is.
/// </summary>
public record Error(ErrorType Type, string Message)
{
    public string Code => Type.ToString();

    public static Error NotFound(string message) =>
        new(ErrorType.NotFound, message);

    public static Error Unauthorized(string message) =>
        new(ErrorType.Unauthorized, message);

    public static Error BadRequest(string message) =>
        new(ErrorType.BadRequest, message);

    public static Error Forbidden(string message) =>
        new(ErrorType.Forbidden, message);

    public static Error Validation(string message) =>
        new(ErrorType.Validation, message);

    public static Error Conflict(string message) =>
        new(ErrorType.Conflict, message);

    public static Error Internal(string message) =>
        new(ErrorType.Internal, message);
}
