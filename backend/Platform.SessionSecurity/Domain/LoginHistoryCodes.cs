// IDN-22 TIM 09
namespace Platform.SessionSecurity.Domain;

/// <summary>IDN-22: stable outcome codes of a personal login history search.</summary>
public static class LoginHistoryCodes
{
    public const string Ok = "Ok";
    public const string InvalidRequest = "InvalidRequest";
    public const string NotAuthenticated = "NotAuthenticated";
    public const string InvalidTimeRange = "InvalidTimeRange";
    public const string RangeTooLarge = "RangeTooLarge";
    public const string InvalidPage = "InvalidPage";
    public const string InvalidPageSize = "InvalidPageSize";
}
