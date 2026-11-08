using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Domain;
using static Platform.AssignmentPrerequisites.Tests.Support.TestData;

namespace Platform.AssignmentPrerequisites.Tests;

[TestFixture]
public class CompletionPolicyTests
{
    [TestCase(AttemptState.Started)]
    [TestCase(AttemptState.InProgress)]
    [TestCase(AttemptState.Submitted)]
    [TestCase(AttemptState.InEvaluation)]
    [TestCase(AttemptState.AwaitingManualReview)]
    [TestCase(AttemptState.Expired)]
    [TestCase(AttemptState.Cancelled)]
    public void AttemptThatIsNotCompleted_DoesNotCount(AttemptState state)
    {
        AttemptOutcomeSnapshot attempt = Attempt(AssignmentA, state: state);

        Assert.That(CompletionPolicy.CountsAsDone(attempt, CompletionRequirement.Passed), Is.False);
    }

    [TestCase(EvaluationStatus.Pending)]
    [TestCase(EvaluationStatus.Running)]
    [TestCase(EvaluationStatus.ManualReview)]
    [TestCase(EvaluationStatus.TechnicalError)]
    public void EvaluationThatIsNotCompleted_DoesNotCount(EvaluationStatus status)
    {
        AttemptOutcomeSnapshot attempt = Attempt(AssignmentA, evaluationStatus: status);

        Assert.That(CompletionPolicy.CountsAsDone(attempt, CompletionRequirement.Passed), Is.False);
    }

    [Test]
    public void MissingEvaluation_DoesNotCount()
    {
        AttemptOutcomeSnapshot attempt = Attempt(AssignmentA, evaluationStatus: null, outcome: null);

        Assert.That(CompletionPolicy.CountsAsDone(attempt, CompletionRequirement.Passed), Is.False);
    }

    [TestCase(EvaluationOutcome.Passed, CompletionRequirement.Passed, true)]
    [TestCase(EvaluationOutcome.Partial, CompletionRequirement.Passed, false)]
    [TestCase(EvaluationOutcome.Failed, CompletionRequirement.Passed, false)]
    [TestCase(EvaluationOutcome.Passed, CompletionRequirement.PassedOrPartial, true)]
    [TestCase(EvaluationOutcome.Partial, CompletionRequirement.PassedOrPartial, true)]
    [TestCase(EvaluationOutcome.Failed, CompletionRequirement.PassedOrPartial, false)]
    public void Outcome_IsComparedWithRequirement(
        EvaluationOutcome outcome,
        CompletionRequirement requirement,
        bool expected)
    {
        AttemptOutcomeSnapshot attempt = Attempt(AssignmentA, outcome: outcome);

        Assert.That(CompletionPolicy.CountsAsDone(attempt, requirement), Is.EqualTo(expected));
    }

    [Test]
    public void AttemptOfAnotherStudent_DoesNotUnlock()
    {
        AttemptOutcomeSnapshot othersAttempt = Attempt(AssignmentA, student: OtherStudent);

        bool satisfied = CompletionPolicy.IsSatisfied(Student, Requires(AssignmentB, AssignmentA), [othersAttempt]);

        Assert.That(satisfied, Is.False);
    }

    [Test]
    public void AttemptOfAnotherAssignment_DoesNotUnlock()
    {
        AttemptOutcomeSnapshot attempt = Attempt(AssignmentC);

        bool satisfied = CompletionPolicy.IsSatisfied(Student, Requires(AssignmentB, AssignmentA), [attempt]);

        Assert.That(satisfied, Is.False);
    }

    [Test]
    public void OwnPassedAttempt_Unlocks()
    {
        bool satisfied = CompletionPolicy.IsSatisfied(
            Student, Requires(AssignmentB, AssignmentA), [Attempt(AssignmentA)]);

        Assert.That(satisfied, Is.True);
    }

    [Test]
    public void LaterFailedAttempt_DoesNotLockAgain()
    {
        AttemptOutcomeSnapshot[] attempts =
        [
            Attempt(AssignmentA, outcome: EvaluationOutcome.Passed),
            Attempt(AssignmentA, outcome: EvaluationOutcome.Failed)
        ];

        Assert.That(CompletionPolicy.IsSatisfied(Student, Requires(AssignmentB, AssignmentA), attempts), Is.True);
    }

    [Test]
    public void NoAttempts_IsNotDone()
    {
        Assert.That(CompletionPolicy.IsSatisfied(Student, Requires(AssignmentB, AssignmentA), []), Is.False);
    }

    [Test]
    public void FindMissing_ReturnsOnlyUnfinishedPrerequisites()
    {
        AssignmentPrerequisite[] prerequisites =
        [
            Requires(AssignmentC, AssignmentA),
            Requires(AssignmentC, AssignmentB)
        ];

        IReadOnlyList<Guid> missing = CompletionPolicy.FindMissing(
            Student, prerequisites, [Attempt(AssignmentA)]);

        Assert.That(missing, Is.EqualTo(new[] { AssignmentB }));
    }

    [Test]
    public void FindMissing_WithoutPrerequisites_IsUnlocked()
    {
        Assert.That(CompletionPolicy.FindMissing(Student, [], []), Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("student@example.com")]
    public void InvalidStudentId_IsRejected(string? studentId)
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() =>
                CompletionPolicy.IsSatisfied(studentId!, Requires(AssignmentB, AssignmentA), []));
            Assert.Throws<ArgumentException>(() =>
                CompletionPolicy.FindMissing(studentId!, [], []));
        });
    }
}
