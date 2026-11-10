using Platform.AssignmentOverview.Application;
using Platform.AssignmentOverview.Domain;
using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Domain;
using Platform.AssignmentPrerequisites.Infrastructure;

namespace Platform.AssignmentOverview.Tests;

// ASN-07 TIM 16: prikaz „zaključano“ u listi dodela.
// Zaključanost računa server, po istom pravilu „urađeno“ kao provera pri startu (CompletionPolicy).
[TestFixture]
public sealed class AssignmentLockServiceTests
{
    private const string Course = "course-oib";
    private const string Student = "student-ana";
    private const string OtherStudent = "student-marko";
    private const string Teacher = "teacher-1";

    private static readonly Guid Lab1 = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Lab2 = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid HiddenLab = Guid.Parse("10000000-0000-0000-0000-000000000009");

    private InMemoryAssignmentPrerequisiteStore _prerequisites = null!;
    private FakeAttemptOutcomeReader _attempts = null!;
    private AssignmentLockService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _prerequisites = new InMemoryAssignmentPrerequisiteStore();
        _attempts = new FakeAttemptOutcomeReader();
        _service = new AssignmentLockService(_prerequisites, _attempts);
    }

    // Pozitivan: dodela bez preduslova nije zaključana.
    [Test]
    public async Task AssignmentWithoutPrerequisites_IsUnlocked()
    {
        var result = await _service.GetLockStatusesAsync(Student, [View(Lab1)]);

        Assert.That(result[Lab1].IsLocked, Is.False);
        Assert.That(result[Lab1].MissingAssignmentIds, Is.Empty);
    }

    // Pozitivan: položen preduslov otključava dodelu.
    [Test]
    public async Task PassedPrerequisite_UnlocksAssignment()
    {
        await RequireAsync(Lab2, Lab1);
        _attempts.Add(Student, Lab1, EvaluationOutcome.Passed);

        var result = await _service.GetLockStatusesAsync(Student, [View(Lab1), View(Lab2)]);

        Assert.That(result[Lab2].IsLocked, Is.False);
    }

    // Negativan: bez položenog preduslova dodela je zaključana i vidi se šta fali.
    [Test]
    public async Task MissingPrerequisite_LocksAssignment_AndNamesIt()
    {
        await RequireAsync(Lab2, Lab1);

        var result = await _service.GetLockStatusesAsync(Student, [View(Lab1), View(Lab2)]);

        Assert.That(result[Lab2].IsLocked, Is.True);
        Assert.That(result[Lab2].MissingAssignmentIds, Is.EquivalentTo(new[] { Lab1 }));
    }

    // Negativan: predat, a još neocenjen pokušaj se ne računa kao urađen.
    [Test]
    public async Task SubmittedButNotEvaluatedPrerequisite_StaysLocked()
    {
        await RequireAsync(Lab2, Lab1);
        _attempts.AddInEvaluation(Student, Lab1);

        var result = await _service.GetLockStatusesAsync(Student, [View(Lab1), View(Lab2)]);

        Assert.That(result[Lab2].IsLocked, Is.True);
    }

    // Negativan: tuđi položen pokušaj ne otključava dodelu ovom studentu.
    [Test]
    public async Task OtherStudentsPassedAttempt_DoesNotUnlock()
    {
        await RequireAsync(Lab2, Lab1);
        _attempts.Add(OtherStudent, Lab1, EvaluationOutcome.Passed);

        var result = await _service.GetLockStatusesAsync(Student, [View(Lab1), View(Lab2)]);

        Assert.That(result[Lab2].IsLocked, Is.True);
    }

    // Negativan: preduslov koji student ne vidi zaključava dodelu, ali se ne otkriva (ASN-10).
    [Test]
    public async Task HiddenPrerequisite_LocksButIsNotRevealed()
    {
        await RequireAsync(Lab2, HiddenLab);

        var result = await _service.GetLockStatusesAsync(Student, [View(Lab1), View(Lab2)]);

        Assert.That(result[Lab2].IsLocked, Is.True);
        Assert.That(result[Lab2].MissingAssignmentIds, Is.Empty);
    }

    // Negativan: pokušaji se traže samo za studenta iz sesije, nikad za drugog.
    [Test]
    public async Task Attempts_AreRequestedOnlyForGivenStudent()
    {
        await RequireAsync(Lab2, Lab1);

        await _service.GetLockStatusesAsync(Student, [View(Lab1), View(Lab2)]);

        Assert.That(_attempts.RequestedStudentIds, Is.EquivalentTo(new[] { Student }));
    }

    // Negativan: bez ID-ja studenta servis odbija rad.
    [Test]
    public void EmptyStudentId_IsRejected()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.GetLockStatusesAsync("", [View(Lab1)]));
    }

    private async Task RequireAsync(Guid assignmentId, Guid requiredAssignmentId)
    {
        await _prerequisites.AddAsync(new AssignmentPrerequisite(
            Course,
            assignmentId,
            requiredAssignmentId,
            CompletionRequirement.Passed,
            Teacher,
            DateTimeOffset.UtcNow));
    }

    private static StudentAssignmentView View(Guid assignmentId) =>
        new(assignmentId, Course, "rev-test", null, DateTimeOffset.UtcNow.AddDays(7), 3);

    // Lažni izvor pokušaja. Namerno vraća i tuđe pokušaje,
    // da bi test dokazao da ih servis ne računa ovom studentu.
    private sealed class FakeAttemptOutcomeReader : IAttemptOutcomeReader
    {
        private readonly List<AttemptOutcomeSnapshot> _attempts = [];

        public List<string> RequestedStudentIds { get; } = [];

        public void Add(string studentId, Guid assignmentId, EvaluationOutcome outcome) =>
            _attempts.Add(new AttemptOutcomeSnapshot(
                Guid.NewGuid(),
                assignmentId,
                studentId,
                AttemptState.Completed,
                EvaluationStatus.Completed,
                outcome));

        public void AddInEvaluation(string studentId, Guid assignmentId) =>
            _attempts.Add(new AttemptOutcomeSnapshot(
                Guid.NewGuid(),
                assignmentId,
                studentId,
                AttemptState.InEvaluation,
                EvaluationStatus.Running,
                null));

        public ValueTask<IReadOnlyList<AttemptOutcomeSnapshot>> GetForStudentAsync(
            string studentId,
            IReadOnlyCollection<Guid> assignmentIds,
            CancellationToken cancellationToken = default)
        {
            RequestedStudentIds.Add(studentId);

            IReadOnlyList<AttemptOutcomeSnapshot> result = _attempts
                .Where(a => assignmentIds.Contains(a.AssignmentId))
                .ToList();

            return ValueTask.FromResult(result);
        }
    }
}