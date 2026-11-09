using Platform.AssignmentOverview.Prerequisites;
using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Domain;
using Platform.AssignmentPrerequisites.Infrastructure;
using Platform.Assignments.Domain.Models;
 
namespace Platform.AssignmentOverview.Tests.StartCheck;
 
// ASN-07 TIM 16
// Kriterijum prihvatanja: "Uslov se proverava na serveru, ne samo u interfejsu."
[TestFixture]
public sealed class AssignmentPrerequisiteGateTests
{
    private const string Ana = "ana";
    private const string Marko = "marko";
 
    private Assignment _first = null!;   // zadatak koji mora prvo
    private Assignment _second = null!;  // zakljucan zadatak (zahteva _first)
    private InMemoryAssignmentPrerequisiteStore _store = null!;
    private InMemoryAttemptOutcomeReader _outcomes = null!;
    private AssignmentPrerequisiteGate _gate = null!;
 
    [SetUp]
    public async Task SetUp()
    {
        _first = NewAssignment();
        _second = NewAssignment();
        _store = new InMemoryAssignmentPrerequisiteStore();
        _outcomes = new InMemoryAttemptOutcomeReader();
 
        await _store.AddAsync(new AssignmentPrerequisite(
            "oib", _second.Id, _first.Id, CompletionRequirement.Passed, "teacher", DateTimeOffset.UtcNow));
 
        _gate = new AssignmentPrerequisiteGate(_store, _outcomes);
    }
 
    // Pozitivan: zadatak bez preduslova je otkljucan.
    [Test]
    public async Task Assignment_WithoutPrerequisites_IsUnlocked()
    {
        var result = await _gate.CheckAsync(Ana, _first);
 
        Assert.That(result.IsUnlocked, Is.True);
        Assert.That(result.MissingAssignmentIds, Is.Empty);
    }
 
    // Negativan: preduslov nije uradjen, zadatak je zakljucan i kaze sta fali.
    [Test]
    public async Task Assignment_WhenPrerequisiteNotCompleted_IsLocked()
    {
        var result = await _gate.CheckAsync(Ana, _second);
 
        Assert.That(result.IsUnlocked, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(PrerequisiteReasonCodes.NotMet));
        Assert.That(result.MissingAssignmentIds, Is.EqualTo(new[] { _first.Id }));
    }
 
    // Negativan: zadatak koji je uradio drugi student ne otkljucava ovom studentu.
    [Test]
    public async Task OtherStudentsCompletion_DoesNotUnlock()
    {
        _outcomes.AddPassed(Marko, _first.Id);
 
        var result = await _gate.CheckAsync(Ana, _second);
 
        Assert.That(result.IsUnlocked, Is.False);
    }
 
    // Pozitivan: uspesno zavrsen preduslov otkljucava.
    [Test]
    public async Task Assignment_WhenPrerequisitePassed_IsUnlocked()
    {
        _outcomes.AddPassed(Ana, _first.Id);
 
        var result = await _gate.CheckAsync(Ana, _second);
 
        Assert.That(result.IsUnlocked, Is.True);
    }
 
    // Negativan: predato, ali jos neocenjeno ne otkljucava.
    [Test]
    public async Task SubmittedButNotEvaluated_DoesNotUnlock()
    {
        _outcomes.Add(new AttemptOutcomeSnapshot(
            Guid.NewGuid(), _first.Id, Ana,
            AttemptState.InEvaluation, EvaluationStatus.Pending, null));
 
        var result = await _gate.CheckAsync(Ana, _second);
 
        Assert.That(result.IsUnlocked, Is.False);
    }
 
    // Negativan: tehnicka greska evaluatora ne otkljucava (EVAL-06).
    [Test]
    public async Task TechnicalError_DoesNotUnlock()
    {
        _outcomes.Add(new AttemptOutcomeSnapshot(
            Guid.NewGuid(), _first.Id, Ana,
            AttemptState.Completed, EvaluationStatus.TechnicalError, null));
 
        var result = await _gate.CheckAsync(Ana, _second);
 
        Assert.That(result.IsUnlocked, Is.False);
    }
 
    // Negativan: neuspesan ishod ne otkljucava.
    [Test]
    public async Task FailedOutcome_DoesNotUnlock()
    {
        _outcomes.Add(new AttemptOutcomeSnapshot(
            Guid.NewGuid(), _first.Id, Ana,
            AttemptState.Completed, EvaluationStatus.Completed, EvaluationOutcome.Failed));
 
        var result = await _gate.CheckAsync(Ana, _second);
 
        Assert.That(result.IsUnlocked, Is.False);
    }
 
    // Negativan: nevalidan student ID, podrazumevano odbijanje (bez izuzetka).
    [TestCase("")]
    [TestCase("ana marko")]
    [TestCase("ana;drop")]
    public async Task InvalidStudentId_IsDenied(string studentId)
    {
        var result = await _gate.CheckAsync(studentId, _first);
 
        Assert.That(result.IsUnlocked, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(PrerequisiteReasonCodes.InvalidStudent));
    }
 
    private static Assignment NewAssignment()
        => new(
            Guid.NewGuid(),
            "oib",
            "revision-" + Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(7),
            3);
}