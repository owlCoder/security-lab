namespace Platform.Authoring.Domain;

/// <summary>
/// Working-copy states from the revision life cycle (figure 4.5.1).
/// Only <see cref="Draft"/> can be edited. A published revision is a separate,
/// immutable LabRevision (REV-01), so Published/Archived do not exist on the working copy.
/// Transitions between these states belong to the REV-* requirements, not to AUT-01.
/// </summary>
public enum DraftStatus
{
    Draft = 1,
    InReview = 2,
}
