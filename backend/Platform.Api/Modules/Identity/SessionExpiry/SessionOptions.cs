namespace Platform.Api.Modules.Identity.SessionExpiry;

public sealed class SessionOptions
{
    public const string Section = "Session";
    public int AbsoluteLifetimeMinutes { get; set; } = 480;
    public int ActivityTimeoutMinutes { get; set; } = 45;
    public int MfaPendingTimeoutMinutes { get; set; } = 5;
}