namespace Platform.Api.Modules.Course
{
    public enum CourseStatus
    {
        Active,
        Archived
    }

    public class Course
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty; 
        public CourseStatus Status { get; set; } = CourseStatus.Active;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    }
}
