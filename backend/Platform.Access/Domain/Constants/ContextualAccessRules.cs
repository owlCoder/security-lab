using Platform.Access.Application.Rules;
using Platform.Access.Domain.Models;

namespace Platform.Access.Domain.Constants;

public static class ContextualAccessRules
{
    public static IReadOnlyDictionary<string, RuleSet<EvaluationSnapshot>> Create()
    {
        return new Dictionary<string, RuleSet<EvaluationSnapshot>>(
            StringComparer.Ordinal)
        {
            // attempt.create:
            // Aktivna dodela, rok nije istekao, ima preostalih pokušaja.
            [AccessActions.AttemptCreate.Id] = new RuleSet<EvaluationSnapshot>(
                [
                    new Rule<EvaluationSnapshot>(
                        "rule.attempt.create.assignment-active",
                        SnapshotConditions.AttrEquals("AssignmentActive", true)),

                    new Rule<EvaluationSnapshot>(
                        "rule.attempt.create.deadline",
                        SnapshotConditions.NowBefore("Deadline")),

                    new Rule<EvaluationSnapshot>(
                        "rule.attempt.create.attempts-remaining",
                        SnapshotConditions.LessThan("AttemptsUsed", "MaxAttempts"))
                ],
                [
                    new Rule<EvaluationSnapshot>(
                        "rule.attempt.create.course-not-archived",
                        SnapshotConditions.AttrEquals("CourseArchived", true))
                ]),

            // attempt.submit:
            // Vlasništvo proverava G7, status proverava G8.
            [AccessActions.AttemptSubmit.Id] = new RuleSet<EvaluationSnapshot>(
                [
                    new Rule<EvaluationSnapshot>(
                        "rule.attempt.submit.in-progress",
                        SnapshotConditions.StatusIn("InProgress"))
                ]),

            // attempt.read.course:
            // Uloga i delegacija proveravaju se kroz G6/G7.
            [AccessActions.AttemptReadCourse.Id] = new RuleSet<EvaluationSnapshot>(
                [
                    new Rule<EvaluationSnapshot>(
                        "rule.attempt.read-course.awaiting-manual-review",
                        SnapshotConditions.StatusIn("AwaitingManualReview"))
                ]),

            // attempt.review:
            // Uloga i delegacija proveravaju se kroz G6/G7.
            [AccessActions.AttemptReview.Id] = new RuleSet<EvaluationSnapshot>(
                [
                    new Rule<EvaluationSnapshot>(
                        "rule.attempt.review.awaiting-manual-review",
                        SnapshotConditions.StatusIn("AwaitingManualReview"))
                ])
        };
    }
}