namespace Platform.LabVisibility.Infrastructure.Persistence;

// Red u lab_public_content. Samo ono što student sme da vidi.
public sealed class LabPublicContentRow
{
    public Guid LabId { get; set; }
    public Guid CourseId { get; set; }
    public Guid AuthorId { get; set; }
    public string State { get; set; } = "";
    public string Title { get; set; } = "";
    public string Goal { get; set; } = "";
    public List<LabPublicTaskRow> Tasks { get; set; } = [];
}

// Red u lab_public_task. Tekst zadatka i opcije, bez tačnog odgovora.
public sealed class LabPublicTaskRow
{
    public Guid LabId { get; set; }
    public string TaskId { get; set; } = "";
    public int Order { get; set; }
    public string Prompt { get; set; } = "";
    public List<string> Options { get; set; } = []; // Npgsql ovo čuva kao text[]
}

// Red u lab_evaluator_key. JEDINA klasa u bazi koja ima tačan odgovor.
public sealed class EvaluatorKeyRow
{
    public Guid LabId { get; set; }
    public string TaskId { get; set; } = "";
    public string ExpectedAnswer { get; set; } = "";

    // Odgovor nikad ne ide u log, ni slučajno (AUD-03).
    public override string ToString() => $"EvaluatorKeyRow {{ LabId = {LabId}, TaskId = {TaskId}, ExpectedAnswer = [REDACTED] }}";
}
