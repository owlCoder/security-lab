using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Infrastructure.Persistence;

// Prevodi redove iz baze u domenske objekte i nazad.
// Domenske klase imaju validaciju, pa podatak iz baze prolazi kroz Create(...) kao i svaki drugi.
internal static class LabVisibilityMapping
{
    public static LabPublicContent ToDomain(LabPublicContentRow row)
    {
        var tasks = row.Tasks
            .Select(t => new PublicTask(t.TaskId, t.Order, t.Prompt, t.Options))
            .ToArray();

        return LabPublicContent.Create(
            row.LabId,
            row.CourseId,
            row.AuthorId,
            Enum.Parse<LabVisibilityState>(row.State),
            row.Title,
            row.Goal,
            tasks);
    }

    public static LabPublicContentRow ToRow(LabPublicContent content) => new()
    {
        LabId = content.LabId,
        CourseId = content.CourseId,
        AuthorId = content.AuthorId,
        State = content.State.ToString(),
        Title = content.Title,
        Goal = content.Goal,
        Tasks = content.Tasks
            .Select(t => new LabPublicTaskRow
            {
                LabId = content.LabId,
                TaskId = t.TaskId,
                Order = t.Order,
                Prompt = t.Prompt,
                Options = t.Options.ToList()
            })
            .ToList()
    };

    public static List<EvaluatorKeyRow> ToRows(EvaluatorKey key) => key.Answers
        .Select(a => new EvaluatorKeyRow
        {
            LabId = key.LabId,
            TaskId = a.TaskId,
            ExpectedAnswer = a.ExpectedAnswer
        })
        .ToList();
}
