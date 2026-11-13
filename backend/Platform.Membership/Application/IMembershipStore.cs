
using Platform.Membership.Domain;

namespace Platform.Membership.Application;

/// <summary>
/// Čuvanje članstava. Pravilo "nema duplog aktivnog upisa" čuva baza (jedinstveni indeks),
/// a AddAsync ga prijavljuje kao AlreadyActive, i kada dva zahteva stignu istovremeno.
/// </summary>
public interface IMembershipStore
{
    ValueTask<CourseMember?> FindActiveAsync(
        Guid courseId,
        Guid accountId,
        CancellationToken cancellationToken = default);

    ValueTask<AddMemberResult> AddAsync(
        CourseMember member,
        CancellationToken cancellationToken = default);
}

public enum AddMemberResult
{
    Added,
    AlreadyActive
}
