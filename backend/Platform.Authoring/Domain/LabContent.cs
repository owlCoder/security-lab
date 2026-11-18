namespace Platform.Authoring.Domain;

/// <summary>
/// The editable content of a laboratory draft. Immutable and always valid:
/// the only way to obtain an instance is <see cref="Create"/>, and the setters are private,
/// so neither object initialisers nor <c>with</c> expressions can bypass validation.
/// Because it is immutable, copying it (draft edit, template copy) can never leak a change
/// into another draft, a template, or an earlier snapshot (AUT-01, AUT-06, REV-03).
/// </summary>
public sealed record LabContent
{
    public const int MaxTitleLength = 120;
    public const int MaxGoalLength = 2000;

    public string Title { get; private init; } = "";
    public string Goal { get; private init; } = "";
    public LabType Type { get; private init; }
    public LabStep[] Steps { get; private init; } = Array.Empty<LabStep>();
    private LabContent() { }

    /// <summary>
    /// A draft may be incomplete (empty goal); completeness is checked at publication (REV-02, AUT-03).
    /// </summary>
    /// <exception cref="DomainRuleException">INVALID_TITLE, INVALID_GOAL or INVALID_LAB_TYPE.</exception>
    public static LabContent Create(string? title, string? goal, LabType type, IEnumerable<LabStep>? steps = null)
    {
        var cleanTitle = title?.Trim() ?? "";
        if (cleanTitle.Length == 0 || cleanTitle.Length > MaxTitleLength || cleanTitle.Any(char.IsControl))
            throw new DomainRuleException(AuthoringErrorCodes.InvalidTitle);

        var cleanGoal = goal?.Trim() ?? "";
        if (cleanGoal.Length > MaxGoalLength || cleanGoal.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new DomainRuleException(AuthoringErrorCodes.InvalidGoal);

        if (!Enum.IsDefined(type))
            throw new DomainRuleException(AuthoringErrorCodes.InvalidLabType);

        var stepList = steps?.ToArray() ?? Array.Empty<LabStep>();

        if (stepList.Any(step => step is null))
        {
            throw new DomainRuleException(AuthoringErrorCodes.InvalidStepSequence);
        }

        var orderedSteps = stepList
            .OrderBy(step => step.SequenceOrder)
            .ToArray();

        for (int i = 0; i < orderedSteps.Length; i++)
        {
            if (orderedSteps[i].SequenceOrder != i + 1)
            {
                throw new DomainRuleException(AuthoringErrorCodes.InvalidStepSequence);
            }
        }

        return new LabContent { Title = cleanTitle, Goal = cleanGoal, Type = type, Steps = orderedSteps };
    }
}