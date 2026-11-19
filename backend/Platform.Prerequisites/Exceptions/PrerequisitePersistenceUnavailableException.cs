namespace Platform.Prerequisites.Exceptions;

public sealed class PrerequisitePersistenceUnavailableException : Exception
{
    public PrerequisitePersistenceUnavailableException()
        : base("Prerequisite persistence is unavailable.")
    {
    }
}