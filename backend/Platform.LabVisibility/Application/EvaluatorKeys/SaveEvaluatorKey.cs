using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Application.EvaluatorKeys;

public sealed class SaveEvaluatorKey(
    IAccessDecision access,
    ILabPublicContentReader labs,
    ILabPublicationSource publications,
    IEvaluatorKeyStore keys,
    ILabVisibilityAudit audit) : ISaveEvaluatorKey
{
    public async ValueTask<SaveEvaluatorKeyResult> ExecuteAsync(
        SaveEvaluatorKeyCommand command,
        SubjectRef? subject,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var decision = await access.DecideAsync(
            new AccessRequest(
                subject,
                LabVisibilityActions.KeyWrite,
                new ResourceRef("lab", command.LabId.ToString()),
                correlationId),
            cancellationToken);

        SaveEvaluatorKeyResult result;

        if (decision.Outcome != AccessOutcome.Allow)
        {
            result = await GetDeniedResultAsync(
                decision.DenialReason,
                command.LabId,
                subject,
                correlationId,
                cancellationToken);
        }
        else
        {
            result = await SaveDraftKeyAsync(command, cancellationToken);
        }

        await audit.WriteAsync(
            new LabVisibilityAuditEvent(
                LabVisibilityActions.KeyWrite,
                command.LabId,
                subject?.Id,
                result.Outcome.ToString(),
                correlationId),
            cancellationToken);

        return result;
    }

    private async ValueTask<SaveEvaluatorKeyResult> GetDeniedResultAsync(
        AccessDenialReason reason,
        Guid labId,
        SubjectRef? subject,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (reason == AccessDenialReason.Unauthenticated)
            return new SaveEvaluatorKeyResult(SaveEvaluatorKeyOutcome.Unauthenticated);

        var hidden = new SaveEvaluatorKeyResult(SaveEvaluatorKeyOutcome.NotFound);

        if (reason != AccessDenialReason.PolicyDenied)
            return hidden;

        // Stanje laboratorije sme da sazna samo nastavnik koji ima pravo pregleda.
        var preview = await access.DecideAsync(
            new AccessRequest(
                subject,
                LabVisibilityActions.Preview,
                new ResourceRef("lab", labId.ToString()),
                correlationId),
            cancellationToken);

        if (preview.Outcome != AccessOutcome.Allow)
            return hidden;

        var publication = await publications.GetAsync(labId, cancellationToken);
        if (publication is null)
            return hidden;

        if (publication.Status is LabPublicationStatus.Published or LabPublicationStatus.Archived)
            return new SaveEvaluatorKeyResult(SaveEvaluatorKeyOutcome.Published);

        var lab = await labs.GetAsync(labId, cancellationToken);
        if (lab?.State is LabVisibilityState.Published or LabVisibilityState.Archived)
            return new SaveEvaluatorKeyResult(SaveEvaluatorKeyOutcome.Published);

        return hidden;
    }

    private async ValueTask<SaveEvaluatorKeyResult> SaveDraftKeyAsync(
        SaveEvaluatorKeyCommand command,
        CancellationToken cancellationToken)
    {
        var lab = await labs.GetAsync(command.LabId, cancellationToken);
        if (lab is null)
            return new SaveEvaluatorKeyResult(SaveEvaluatorKeyOutcome.NotFound);

        if (lab.State != LabVisibilityState.Draft)
            return new SaveEvaluatorKeyResult(SaveEvaluatorKeyOutcome.Published);

        if (command.Answers is null)
            return Invalid(SaveEvaluatorKeyErrors.MissingTask);

        var remainingTasks = new HashSet<string>(
            lab.Tasks.Select(task => task.TaskId),
            StringComparer.Ordinal);
        var seenTasks = new HashSet<string>(StringComparer.Ordinal);
        var answers = new List<TaskAnswerKey>();

        foreach (var input in command.Answers)
        {
            var taskId = input?.TaskId?.Trim();
            if (input is null || string.IsNullOrEmpty(taskId))
                return Invalid(SaveEvaluatorKeyErrors.UnknownTask);

            if (!seenTasks.Add(taskId))
                return Invalid(SaveEvaluatorKeyErrors.DuplicateTask);

            if (!remainingTasks.Remove(taskId))
                return Invalid(SaveEvaluatorKeyErrors.UnknownTask);

            if (string.IsNullOrWhiteSpace(input.ExpectedAnswer))
                return Invalid(SaveEvaluatorKeyErrors.EmptyAnswer);

            if (input.ExpectedAnswer.Length > TaskAnswerKey.MaxExpectedAnswerLength)
                return Invalid(SaveEvaluatorKeyErrors.AnswerTooLong);

            answers.Add(new TaskAnswerKey(taskId, input.ExpectedAnswer));
        }

        if (remainingTasks.Count > 0)
            return Invalid(SaveEvaluatorKeyErrors.MissingTask);

        var key = EvaluatorKey.Create(lab, answers);
        await keys.SaveAsync(key, cancellationToken);

        return new SaveEvaluatorKeyResult(SaveEvaluatorKeyOutcome.Saved);
    }

    private static SaveEvaluatorKeyResult Invalid(string code) =>
        new(SaveEvaluatorKeyOutcome.Invalid, code);
}