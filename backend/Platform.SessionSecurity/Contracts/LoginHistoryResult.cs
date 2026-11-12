using Platform.SessionSecurity.Domain;

// IDN-22 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-22: outcome of a personal login history search. A valid filter without matches is a
/// success with an empty list, not an error.
/// </summary>
public sealed record LoginHistoryResult
{
    private LoginHistoryResult(
        bool succeeded, string code, IReadOnlyList<LoginHistoryItem> items, int page, int pageSize)
    {
        Succeeded = succeeded;
        Code = code;
        Items = items;
        Page = page;
        PageSize = pageSize;
    }

    public bool Succeeded { get; }

    /// <summary>One of <see cref="LoginHistoryCodes"/>.</summary>
    public string Code { get; }

    public IReadOnlyList<LoginHistoryItem> Items { get; }

    public int Page { get; }

    public int PageSize { get; }

    public static LoginHistoryResult Ok(IReadOnlyList<LoginHistoryItem> items, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(true, LoginHistoryCodes.Ok, items, page, pageSize);
    }

    public static LoginHistoryResult Rejected(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (code == LoginHistoryCodes.Ok)
        {
            throw new ArgumentException("A rejection cannot use the success code.", nameof(code));
        }

        return new(false, code, [], 0, 0);
    }
}
