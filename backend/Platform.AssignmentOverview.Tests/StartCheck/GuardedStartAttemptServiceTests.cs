using Platform.AssignmentOverview.Prerequisites;
using Platform.AssignmentOverview.Tests.Support;
using Platform.AssignmentPrerequisites.Domain;
using Platform.AssignmentPrerequisites.Infrastructure;
using Platform.Assignments.Application;
using Platform.Assignments.Domain.Models;
 
namespace Platform.AssignmentOverview.Tests.StartCheck;
 
// ASN-07 TIM 16
// Prvi zaokruzen tok: pokretanje pokusaja prolazi kroz proveru preduslova na serveru.
[TestFixture]
public sealed class GuardedStartAttemptServiceTests
{
    private const string Ana = "ana";
 
    private Assignment _first = null!;
    private Assignment _locked = null!;
    private InMemoryAssignmentPrerequisiteStore _store = null!;
    private InMemoryAttemptOutcomeReader _outcomes = null!;
    private FakeAssignmentRepository _assignments = null!;
    private StartCheckAttemptStore _attempts = null!;
    private GuardedStartAttemptService _service = null!;
 
    [SetUp]
    public async Task SetUp()
    {
        _first = NewAssignment();
        _locked = NewAssignment();
 
        _store = new InMemoryAssignmentPrerequisiteStore();
        await _store.AddAsync(new AssignmentPrerequisite(
            "oib", _locked.Id, _first.Id, CompletionRequirement.Passed, "teacher", DateTimeOffset.UtcNow));
 
        _outcomes = new InMemoryAttemptOutcomeReader();
        _assignments = new FakeAssignmentRepository();
        _assignments.Add(_first);
        _assignments.Add(_locked);
        _attempts = new StartCheckAttemptStore();
 
        _service = Build(new StartCheckAuthorization(true));
    }
 
    // Negativan: preduslov nije uradjen, pokusaj se NE pravi.
    [Test]
    public void Start_WhenPrerequisiteMissing_Throws_AndCreatesNoAttempt()
    {
        var exception = Assert.ThrowsAsync<PrerequisitesNotMetException>(
            async () => await _service.StartAttemptAsync(_locked.Id, Ana, Guid.NewGuid()));
 
        Assert.That(exception!.ReasonCode, Is.EqualTo(PrerequisiteReasonCodes.NotMet));
        Assert.That(exception.MissingAssignmentIds, Is.EqualTo(new[] { _first.Id }));
        Assert.That(_attempts.StartCalls, Is.EqualTo(0));
    }
 
    // Pozitivan: kad je preduslov uspesno uradjen, pokusaj se pokrece.
    [Test]
    public async Task Start_WhenPrerequisitePassed_CreatesAttempt()
    {
        _outcomes.AddPassed(Ana, _first.Id);
 
        var attemptId = await _service.StartAttemptAsync(_locked.Id, Ana, Guid.NewGuid());
 
        Assert.That(attemptId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(_attempts.StartCalls, Is.EqualTo(1));
    }
 
    // Negativan: student bez prava ne sme da sazna da je zadatak zakljucan.
    [Test]
    public void Start_WhenNotAuthorized_ThrowsAccessDenied_NotPrerequisiteError()
    {
        var service = Build(new StartCheckAuthorization(false));
 
        Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.StartAttemptAsync(_locked.Id, Ana, Guid.NewGuid()));
 
        Assert.That(_attempts.StartCalls, Is.EqualTo(0));
    }
 
    // Negativan: nepostojeca dodela.
    [Test]
    public void Start_WhenAssignmentNotFound_Throws()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await _service.StartAttemptAsync(Guid.NewGuid(), Ana, Guid.NewGuid()));
    }
 
    private GuardedStartAttemptService Build(StartCheckAuthorization authorization)
    {
        var gate = new AssignmentPrerequisiteGate(_store, _outcomes);
        var inner = new StartAttemptService(_assignments, _attempts, authorization);
 
        return new GuardedStartAttemptService(_assignments, authorization, gate, inner);
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