using Platform.Access.Domain.Enums;

namespace Platform.Access.Domain.Models;

public sealed record CourseRolePolicyDefinition(
    ActionDefinition Action,
    IReadOnlySet<CourseRole> AllowedRoles,
    bool AllowAssistantDelegation = false,
    bool RequiresOwnership = false);
