namespace Platform.Prerequisites.Domain;

public enum AddPrerequisiteResult
{
    Added,
    Duplicate,
    WouldCreateCycle
}