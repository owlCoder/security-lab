namespace Platform.Authoring.Domain;

public sealed record LabStep
{
    public const int MaxTitleLength = 120;
    public const int MaxInstructionLength = 5000;

    public int SequenceOrder { get; private init; }
    public string Title { get; private init; } = "";
    public string Instruction { get; private init; } = "";

    private LabStep() { }

    public static LabStep Create(int sequenceOrder, string? title, string? instruction)
    {
        if (sequenceOrder < 1)
            throw new DomainRuleException(AuthoringErrorCodes.InvalidStepSequence);

        var cleanTitle = title?.Trim() ?? "";
        if (cleanTitle.Length == 0 || cleanTitle.Length > MaxTitleLength || cleanTitle.Any(char.IsControl))
            throw new DomainRuleException(AuthoringErrorCodes.InvalidStepTitle);

        var cleanInstruction = instruction?.Trim() ?? "";
        if (cleanInstruction.Length == 0 || cleanInstruction.Length > MaxInstructionLength ||
            cleanInstruction.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new DomainRuleException(AuthoringErrorCodes.InvalidStepInstruction);

        return new LabStep
        {
            SequenceOrder = sequenceOrder,
            Title = cleanTitle,
            Instruction = cleanInstruction
        };
    }
}
