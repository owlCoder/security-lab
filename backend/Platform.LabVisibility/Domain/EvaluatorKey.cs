namespace Platform.LabVisibility.Domain;

// Tačni odgovori ostaju na serveru, odvojeni od javnog sadržaja.
public sealed record EvaluatorKey
{
    public Guid LabId { get; }
    public IReadOnlyList<TaskAnswerKey> Answers { get; }

    private EvaluatorKey(
        Guid labId,
        IReadOnlyList<TaskAnswerKey> answers)
    {
        LabId = labId;
        Answers = answers;
    }

    public static EvaluatorKey Create(
        LabPublicContent content,
        IReadOnlyList<TaskAnswerKey> answers)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(answers);

        var knownTaskIds = new HashSet<string>(
            content.Tasks.Select(task => task.TaskId),
            StringComparer.Ordinal);

        var answersByTaskId =
            new Dictionary<string, TaskAnswerKey>(StringComparer.Ordinal);

        foreach (var answer in answers)
        {
            if (answer is null)
                throw new ArgumentException(
                    "Odgovor ne sme biti null.", nameof(answers));

            if (!knownTaskIds.Contains(answer.TaskId))
                throw new ArgumentException(
                    "Ključ sadrži odgovor za nepoznat zadatak.",
                    nameof(answers));

            if (!answersByTaskId.TryAdd(answer.TaskId, answer))
                throw new ArgumentException(
                    "Zadatak ne sme imati više od jednog odgovora.",
                    nameof(answers));
        }

        if (answersByTaskId.Count != knownTaskIds.Count)
            throw new ArgumentException(
                "Svaki zadatak mora imati tačno jedan odgovor.",
                nameof(answers));

        // Odgovori prate isti redosled kao javni zadaci.
        var orderedAnswers = content.Tasks
            .Select(task => answersByTaskId[task.TaskId])
            .ToArray();

        return new EvaluatorKey(
            content.LabId,
            Array.AsReadOnly(orderedAnswers));
    }

    public override string ToString()
    {
        return "EvaluatorKey { Answers = [REDACTED] }";
    }
}

public sealed record TaskAnswerKey
{
    public const int MaxExpectedAnswerLength = 4096;

    public string TaskId { get; }
    public string ExpectedAnswer { get; }

    public TaskAnswerKey(string taskId, string expectedAnswer)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException(
                "ID zadatka je obavezan.", nameof(taskId));

        if (string.IsNullOrWhiteSpace(expectedAnswer))
            throw new ArgumentException(
                "Tačan odgovor je obavezan.", nameof(expectedAnswer));

        if (expectedAnswer.Length > MaxExpectedAnswerLength)
            throw new ArgumentException(
                "Tačan odgovor je predugačak.", nameof(expectedAnswer));

        TaskId = taskId.Trim();

        // Čuvamo originalni odgovor: razmaci mogu biti deo rešenja.
        ExpectedAnswer = expectedAnswer;
    }

    public override string ToString()
    {
        return "TaskAnswerKey { ExpectedAnswer = [REDACTED] }";
    }
}