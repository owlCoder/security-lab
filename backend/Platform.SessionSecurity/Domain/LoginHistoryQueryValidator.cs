// IDN-22 TIM 09
namespace Platform.SessionSecurity.Domain;

/// <summary>
/// IDN-22 / SEC-04 / SEC-05: checks a login history filter against <see cref="LoginHistoryLimits"/>
/// before any data is read, and fills in missing values. Pure rule: the current time is passed in.
/// Checks run in a fixed order (outcome, time range, page, page size); the first failure wins.
/// </summary>
public static class LoginHistoryQueryValidator
{
    public static LoginHistoryQueryValidationResult Validate(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        LoginOutcomeFilter? outcome,
        int? page,
        int? pageSize,
        DateTimeOffset now)
    {
        if (outcome is { } requestedOutcome && !Enum.IsDefined(requestedOutcome))
        {
            return LoginHistoryQueryValidationResult.Invalid(LoginHistoryCodes.InvalidRequest);
        }

        var resolvedTo = (toUtc ?? now).ToUniversalTime();
        if (fromUtc is null && resolvedTo - DateTimeOffset.MinValue < LoginHistoryLimits.DefaultRange)
        {
            return LoginHistoryQueryValidationResult.Invalid(LoginHistoryCodes.InvalidTimeRange);
        }

        var resolvedFrom = (fromUtc ?? resolvedTo - LoginHistoryLimits.DefaultRange).ToUniversalTime();
        var resolvedPage = page ?? LoginHistoryLimits.FirstPage;
        var resolvedPageSize = pageSize ?? LoginHistoryLimits.DefaultPageSize;

        var code = CheckRange(resolvedFrom, resolvedTo) ?? CheckPaging(resolvedPage, resolvedPageSize);
        if (code is not null)
        {
            return LoginHistoryQueryValidationResult.Invalid(code);
        }

        var skip = (resolvedPage - (long)LoginHistoryLimits.FirstPage) * resolvedPageSize;
        if (skip > int.MaxValue)
        {
            return LoginHistoryQueryValidationResult.Invalid(LoginHistoryCodes.InvalidPage);
        }

        return LoginHistoryQueryValidationResult.Valid(new LoginHistorySearchWindow(
            resolvedFrom, resolvedTo, outcome, resolvedPage, resolvedPageSize, (int)skip));
    }

    private static string? CheckRange(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (fromUtc > toUtc)
        {
            return LoginHistoryCodes.InvalidTimeRange;
        }

        return toUtc - fromUtc > LoginHistoryLimits.MaxRange ? LoginHistoryCodes.RangeTooLarge : null;
    }

    private static string? CheckPaging(int page, int pageSize)
    {
        if (page < LoginHistoryLimits.FirstPage)
        {
            return LoginHistoryCodes.InvalidPage;
        }

        return pageSize is < 1 or > LoginHistoryLimits.MaxPageSize ? LoginHistoryCodes.InvalidPageSize : null;
    }
}
