namespace Platform.Authoring.Domain;

/// <summary>
/// Laboratory scenario types (specification chapter 5, TYP-01..TYP-08).
/// Values start at 1 so that an uninitialised value (0) is never a valid type.
/// </summary>
public enum LabType
{
    AccessControl = 1,
    LogAnalysis = 2,
    SecureConfiguration = 3,
    ThreatModel = 4,
    MfaAndSessions = 5,
    ContextualAuthorization = 6,
    IncidentResponse = 7,
    IsolatedEnvironment = 8,
}
