using Platform.AssignmentOverview.Prerequisites;
using Platform.AssignmentOverview.Tests.Support;
using Platform.Assignments.Application;
using Platform.Assignments.Domain.Models;
using Platform.Prerequisites.Application;
using Platform.Prerequisites.Domain;
using Platform.Prerequisites.Infrastructure;
 
namespace Platform.AssignmentOverview.Tests.StartCheck;
 
// ASN-07 TIM 16
// Prvi zaokruzen tok: pokretanje pokusaja prolazi kroz proveru preduslova na serveru.
[TestFixture]
public sealed class GuardedStartAttemptServiceTests
{
    private readonly Guid _lab1 = Guid.NewGuid();
    private readonly Guid _lab2 = Guid.NewGuid();
    private readonly string _ana = Guid.NewGuid().ToString();
 
    private StubCompletedLaboratories _completed = null!;
    private StartCheckAttemptStore _store = null!;
    private FakeAssignmentRepository _assignments = null!;
    private Assignment _lockedAssignment = null!;
    private GuardedStartAttemptService _service = null!;
 
    [SetUp]
    public async Task SetUp()
    {
        var prerequisites = new InMemoryPrerequisiteRepository();
        _completed = new StubCompletedLaboratories();
        var resolver = new InMemoryRevisionLaboratoryResolver();
        resolver.Map("rev-lab1", _lab1);
        resolver.Map("rev-lab2", _lab2);
        await prerequisites.AddAsync(new LabPrerequisite(_lab2, _lab1));
 
        _lockedAssignment = NewAssignment("rev-lab2");
        _assignments = new FakeAssignmentRepository();
        _assignments.Add(_lockedAssignment);
 
        _store = new StartCheckAttemptStore();
        var authorization = new StartCheckAuthorization(true);
 
        _service = Build(authorization, prerequisites, resolver);
    }
 
    // Negativan: preduslov nije uradjen, pokusaj se NE pravi.
    [Test]
    public void Start_WhenPrerequisiteMissing_Throws_AndCreatesNoAttempt()
    {
        var exception = Assert.ThrowsAsync<PrerequisitesNotMetException>(
            async () => await _service.StartAttemptAsync(
                _lockedAssignment.Id, _ana, Guid.NewGuid()));
 
        Assert.That(exception!.ReasonCode, Is.EqualTo(PrerequisiteReasonCodes.NotMet));
        Assert.That(exception.MissingLaboratoryIds, Is.EqualTo(new[] { _lab1 }));
        Assert.That(_store.StartCalls, Is.EqualTo(0));
    }
 
    // Pozitivan: kad je preduslov uradjen, pokusaj se pokrece.
    [Test]
    public async Task Start_WhenPrerequisiteCompleted_CreatesAttempt()
    {
        _completed.MarkCompleted(Guid.Parse(_ana), _lab1);
 
        var attemptId = await _service.StartAttemptAsync(
            _lockedAssignment.Id, _ana, Guid.NewGuid());
 
        Assert.That(attemptId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(_store.StartCalls, Is.EqualTo(1));
    }
 
    // Negativan: student bez prava ne sme da sazna da je vezba zakljucana.
    [Test]
    public void Start_WhenNotAuthorized_ThrowsAccessDenied_NotPrerequisiteError()
    {
        var service = Build(
            new StartCheckAuthorization(false),
            new InMemoryPrerequisiteRepository(),
            new InMemoryRevisionLaboratoryResolver());
 
        Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.StartAttemptAsync(
                _lockedAssignment.Id, _ana, Guid.NewGuid()));
 
        Assert.That(_store.StartCalls, Is.EqualTo(0));
    }
 
    // Negativan: nepostojeca dodela.
    [Test]
    public void Start_WhenAssignmentNotFound_Throws()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await _service.StartAttemptAsync(
                Guid.NewGuid(), _ana, Guid.NewGuid()));
    }
 
    private GuardedStartAttemptService Build(
        StartCheckAuthorization authorization,
        InMemoryPrerequisiteRepository prerequisites,
        InMemoryRevisionLaboratoryResolver resolver)
    {
        var gate = new AssignmentPrerequisiteGate(
            resolver,
            new PrerequisiteAccessService(prerequisites, _completed));
 
        var inner = new StartAttemptService(_assignments, _store, authorization);
 
        return new GuardedStartAttemptService(_assignments, authorization, gate, inner);
    }
 
    private static Assignment NewAssignment(string revisionId)
        => new(
            Guid.NewGuid(),
            "oib",
            revisionId,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(7),
            3);
}