namespace Platform.AssignmentPrerequisites.Domain;

public enum AddPrerequisiteOutcome
{
    Added = 1,

    Duplicate = 2,

    WouldCreateCycle = 3,

    AssignmentNotInCourse = 4
}
