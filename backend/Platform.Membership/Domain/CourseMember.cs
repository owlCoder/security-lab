
namespace Platform.Membership.Domain;

/// <summary>
/// Jedno članstvo korisnika u kursu (ACC-02). Zapis se nikad ne briše:
/// ispis menja status, a ponovni upis pravi novi zapis.
/// </summary>
public sealed class CourseMember
{
    private CourseMember()
    {
    }

    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid AccountId { get; private set; }
    public MemberRole Role { get; private set; }
    public MemberStatus Status { get; private set; }
    public DateTimeOffset EnrolledAt { get; private set; }
    public Guid EnrolledBy { get; private set; }
    public DateTimeOffset? LeftAt { get; private set; }
    public Guid? LeftBy { get; private set; }

    public static CourseMember Enroll(
        Guid courseId,
        Guid accountId,
        MemberRole role,
        Guid enrolledBy,
        DateTimeOffset now)
    {
        if (courseId == Guid.Empty)
        {
            throw new ArgumentException("Course id is required.", nameof(courseId));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account id is required.", nameof(accountId));
        }

        if (enrolledBy == Guid.Empty)
        {
            throw new ArgumentException("The enrolling account is required.", nameof(enrolledBy));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        return new CourseMember
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            AccountId = accountId,
            Role = role,
            Status = MemberStatus.Active,
            EnrolledAt = now.ToUniversalTime(),
            EnrolledBy = enrolledBy
        };
    }

    /// <summary>
    /// Ispis iz kursa. Koristi se u KT2 (ispis studenta); ovde postoji jer od njega
    /// zavisi pravilo jedinstvenosti: samo aktivan zapis blokira novi upis.
    /// </summary>
    public void Leave(Guid leftBy, DateTimeOffset now)
    {
        if (leftBy == Guid.Empty)
        {
            throw new ArgumentException("The account that ends the membership is required.", nameof(leftBy));
        }

        if (Status != MemberStatus.Active)
        {
            throw new InvalidOperationException("Only an active membership can be ended.");
        }

        Status = MemberStatus.Left;
        LeftAt = now.ToUniversalTime();
        LeftBy = leftBy;
    }
}
