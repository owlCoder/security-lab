namespace Platform.Access.Domain.Constants;

public static class ResourceAttributeKeys
{
    public const string CourseStatus = "CourseStatus";
    public const string LaboratoryStatus = "LaboratoryStatus";
    public const string Availability = "Availability";

    /// <summary>DateTimeOffset, UTC.</summary>
    public const string AvailableFromUtc = "AvailableFromUtc";

    /// <summary>DateTimeOffset, UTC.</summary>
    public const string DueAtUtc = "DueAtUtc";

    /// <summary>bool.</summary>
    public const string AllowStartAfterDeadline = "AllowStartAfterDeadline";

    /// <summary>int.</summary>
    public const string MaxAttempts = "MaxAttempts";

    /// <summary>int.</summary>
    public const string AttemptsUsed = "AttemptsUsed";

    public static class CourseStatuses
    {
        public const string Active = "Active";
        public const string Archived = "Archived";
    }

    public static class LaboratoryStatuses
    {
        public const string Draft = "Draft";
        public const string Archived = "Archived";
    }

    public static class AssignmentStatuses
    {
        public const string Active = "Active";
        public const string Archived = "Archived";
    }

    public static class AssignmentAvailabilities
    {
        public const string Available = "Available";
        public const string NotStarted = "NotStarted";
        public const string Expired = "Expired";
        public const string Archived = "Archived";
    }

    public static class AttemptStatuses
    {
        public const string InProgress = "InProgress";
        public const string Submitted = "Submitted";
        public const string AwaitingManualReview = "AwaitingManualReview";
        public const string Completed = "Completed";
        public const string Expired = "Expired";
        public const string Cancelled = "Cancelled";
    }
}