using System.Net;
using Microsoft.Extensions.Options;

namespace Platform.Api.Modules.Identity.SessionLimiting;


public sealed class SessionOptions {
    //8h, 45min, 5min
    public const string Section = "Session";
    public int AbsoluteLifetimeMinutes { get; set; } = 480;
    public int ActivityTimeoutMinutes { get; set; } = 45;
    public int MfaPendingTimeoutMinutes { get; set; } = 5;
}