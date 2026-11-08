
namespace Platform.Membership.Domain;

/// <summary>
/// Privremena delegirana dozvola (ACC-07): nastavnik kursa daje asistentu jednu nastavničku
/// radnju u tom kursu, do određenog trenutka. Zapis se nikad ne briše: opoziv upisuje ko i kada,
/// a istek se vidi iz ExpiresAt. Tako uvek postoji zapis o promeni prava.
/// </summary>
public sealed class CourseDelegation
{
    public const int MaxActionLength = 64;

    private CourseDelegation()
    {
    }

    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid GranteeId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public Guid GrantedBy { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }

    public static CourseDelegation Grant(
        Guid courseId,
        Guid granteeId,
        string action,
        Guid grantedBy,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        if (courseId == Guid.Empty)
        {
            throw new ArgumentException("Course id is required.", nameof(courseId));
        }

        if (granteeId == Guid.Empty)
        {
            throw new ArgumentException("Grantee id is required.", nameof(granteeId));
        }

        if (grantedBy == Guid.Empty)
        {
            throw new ArgumentException("The granting account is required.", nameof(grantedBy));
        }

        if (granteeId == grantedBy)
        {
            throw new ArgumentException("A delegation cannot be granted to oneself.", nameof(granteeId));
        }

        if (string.IsNullOrWhiteSpace(action) || action.Length > MaxActionLength)
        {
            throw new ArgumentException("A valid action is required.", nameof(action));
        }

        var grantedAt = now.ToUniversalTime();
        var expires = expiresAt.ToUniversalTime();

        if (expires <= grantedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "A delegation must expire in the future.");
        }

        return new CourseDelegation
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            GranteeId = granteeId,
            Action = action,
            GrantedBy = grantedBy,
            GrantedAt = grantedAt,
            ExpiresAt = expires
        };
    }

    public DelegationState StateAt(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return DelegationState.Revoked;
        }

        return now >= ExpiresAt
            ? DelegationState.Expired
            : DelegationState.Active;
    }

    public bool IsActiveAt(DateTimeOffset now) =>
        StateAt(now) == DelegationState.Active;

    /// <summary>Opoziv pre isteka. Zapis ostaje, uz to ko je i kada opozvao.</summary>
    public void Revoke(Guid revokedBy, DateTimeOffset now)
    {
        if (revokedBy == Guid.Empty)
        {
            throw new ArgumentException(
                "The revoking account is required.",
                nameof(revokedBy));
        }

        if (!IsActiveAt(now))
        {
            throw new InvalidOperationException(
                "Only an active delegation can be revoked.");
        }

        RevokedAt = now.ToUniversalTime();
        RevokedBy = revokedBy;
    }
}
