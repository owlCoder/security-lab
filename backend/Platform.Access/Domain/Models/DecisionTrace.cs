using Platform.Access.Domain.Enums;

namespace Platform.Access.Domain.Models;

public sealed record DecisionTrace(IReadOnlyList<TraceStep> Steps);

public sealed record TraceStep(string Id, TraceResult Result);
