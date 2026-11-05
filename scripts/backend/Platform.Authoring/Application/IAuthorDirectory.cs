namespace Platform.Authoring.Application;

/// <summary>
/// Answers "may this account author laboratories?". Temporary port: it is backed by configuration
/// until the course-role contract of the ACC modules (ACC-02/ACC-03) is published; then only the
/// implementation changes, not the use cases or the policy.
/// </summary>
public interface IAuthorDirectory
{
    bool IsAuthor(Guid userId);
}
