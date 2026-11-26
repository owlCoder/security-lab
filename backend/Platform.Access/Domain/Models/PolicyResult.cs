using Platform.Access.Domain.Enums;

namespace Platform.Access.Domain.Models;

public sealed record PolicyResult(
    PolicyOutcome Outcome,
    AccessDenialReason Reason,
    DecisionTrace Trace);
