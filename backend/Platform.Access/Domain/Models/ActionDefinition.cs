using Platform.Access.Domain.Enums;

namespace Platform.Access.Domain.Models;

public sealed record ActionDefinition(
    string Id,
    ActionScope Scope,
    string? ResourceType,
    bool IsPrivileged);
