namespace Platform.Membership.Contracts;

/// <summary>
/// Ono što ACC-02 treba da zna o kursu: da li postoji i da li je arhiviran.
/// Kurs pripada ACC-01 (Tim 21). Dok oni ne izlože svoj ugovor, koristimo
/// privremene implementacije iz Infrastructure/Courses.
/// </summary>
public interface ICourseReader
{
    ValueTask<CourseInfo?> FindAsync(Guid courseId, CancellationToken cancellationToken = default);
}

public sealed record CourseInfo(Guid Id, bool IsArchived);