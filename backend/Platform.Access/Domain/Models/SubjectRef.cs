using Platform.Access.Domain.Enums;

namespace Platform.Access.Domain.Models;

public sealed record SubjectRef(
    string Id,
    AccountState State,
    SystemRole SystemRole = SystemRole.None);
