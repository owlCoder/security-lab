namespace Platform.Identity.Services.Sessions;

/// <summary>Keys under which Identity stores per-request state in <c>HttpContext.Items</c>.</summary>
public static class SessionHttpContextItems
{
    /// <summary>The validated session that the session check accepted for this request.</summary>
    public const string Session = "session";
}
