using Platform.SessionSecurity.Domain;

// IDN-22 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-22: filter of the personal login history. It has no user field on purpose: whose events
/// are searched is decided by the server from <see cref="AuthenticatedSubject"/> (SEC-01).
/// Missing values get the defaults from <see cref="LoginHistoryLimits"/>.
/// </summary>
public sealed record SearchLoginHistoryQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    LoginOutcomeFilter? Outcome = null,
    int? Page = null,
    int? PageSize = null);
