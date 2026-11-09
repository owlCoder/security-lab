namespace Team21.Acc.Core.Common;

/// <summary>
/// Kind of failure. Each one has its HTTP status in <see cref="ResultHttpExtensions"/>.
/// </summary>
public enum ErrorType
{
    NotFound,
    Unauthorized,
    BadRequest,
    Forbidden,
    Validation,
    Conflict,
    Internal
}
