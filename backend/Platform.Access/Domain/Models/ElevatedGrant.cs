using Platform.Access.Domain.Enums;

namespace Platform.Access.Domain.Models;

public sealed record ElevatedGrant(
    Guid Id,
    Guid HolderId,
    Guid IssuerId,
    string Action,
    GrantScope Scope,
    string Reason,
    DateTimeOffset NotBefore,
    DateTimeOffset ExpiresAt,
    bool Revoked);

public sealed record GrantScope(GrantScopeKind Kind, string Id);
