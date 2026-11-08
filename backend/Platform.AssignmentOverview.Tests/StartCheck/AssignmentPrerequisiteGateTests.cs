using Platform.AssignmentOverview.Prerequisites;
using Platform.Assignments.Domain.Models;
using Platform.Prerequisites.Application;
using Platform.Prerequisites.Domain;
using Platform.Prerequisites.Infrastructure;
 
namespace Platform.AssignmentOverview.Tests.StartCheck;
 
// ASN-07 TIM 16
// Kriterijum prihvatanja: "Uslov se proverava na serveru, ne samo u interfejsu."
[TestFixture]
public sealed class AssignmentPrerequisiteGateTests
{
    private readonly Guid _lab1 = Guid.NewGuid();
    private readonly Guid _lab2 = Guid.NewGuid();
    private readonly string _ana = Guid.NewGuid().ToString();
    private readonly string _marko = Guid.NewGuid().ToString();
 
    private InMemoryPrerequisiteRepository _prerequisites = null!;
    private StubCompletedLaboratories _completed = null!;
    private InMemoryRevisionLaboratoryResolver _resolver = null!;
    private AssignmentPrerequisiteGate _gate = null!;
 
    [SetUp]
    public async Task SetUp()
    {
        _prerequisites = new InMemoryPrerequisiteRepository();
        _completed = new StubCompletedLaboratories();
        _resolver = new InMemoryRevisionLaboratoryResolver();
        _resolver.Map("rev-lab1", _lab1);
        _resolver.Map("rev-lab2", _lab2);
 
        // Laboratorija 2 zahteva laboratoriju 1.
        await _prerequisites.AddAsync(new LabPrerequisite(_lab2, _lab1));
 
        _gate = new AssignmentPrerequisiteGate(
            _resolver,
            new PrerequisiteAccessService(_prerequisites, _completed));
    }
 
    // Pozitivan: vezba bez preduslova je otkljucana.
    [Test]
    public async Task Assignment_WithoutPrerequisites_IsUnlocked()
    {
        var result = await _gate.CheckAsync(_ana, NewAssignment("rev-lab1"));
 
        Assert.That(result.IsUnlocked, Is.True);
        Assert.That(result.MissingLaboratoryIds, Is.Empty);
    }
 
    // Negativan: preduslov nije uradjen, vezba je zakljucana i kaze sta fali.
    [Test]
    public async Task Assignment_WhenPrerequisiteNotCompleted_IsLocked()
    {
        var result = await _gate.CheckAsync(_ana, NewAssignment("rev-lab2"));
 
        Assert.That(result.IsUnlocked, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(PrerequisiteReasonCodes.NotMet));
        Assert.That(result.MissingLaboratoryIds, Is.EqualTo(new[] { _lab1 }));
    }
 
    // Negativan: vezbu koju je uradio drugi student ne otkljucava ovom studentu.
    [Test]
    public async Task OtherStudentsCompletion_DoesNotUnlock()
    {
        _completed.MarkCompleted(Guid.Parse(_marko), _lab1);
 
        var result = await _gate.CheckAsync(_ana, NewAssignment("rev-lab2"));
 
        Assert.That(result.IsUnlocked, Is.False);
    }
 
    // Pozitivan: ispunjen uslov propusta.
    [Test]
    public async Task Assignment_WhenPrerequisiteCompleted_IsUnlocked()
    {
        _completed.MarkCompleted(Guid.Parse(_ana), _lab1);
 
        var result = await _gate.CheckAsync(_ana, NewAssignment("rev-lab2"));
 
        Assert.That(result.IsUnlocked, Is.True);
    }
 
    // Negativan: student ID nije Guid, podrazumevano odbijanje.
    [Test]
    public async Task InvalidStudentId_IsDenied()
    {
        var result = await _gate.CheckAsync("ana", NewAssignment("rev-lab1"));
 
        Assert.That(result.IsUnlocked, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(PrerequisiteReasonCodes.InvalidStudent));
    }
 
    // Negativan: prazan Guid nije validan student.
    [Test]
    public async Task EmptyGuidStudent_IsDenied()
    {
        var result = await _gate.CheckAsync(Guid.Empty.ToString(), NewAssignment("rev-lab1"));
 
        Assert.That(result.IsUnlocked, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(PrerequisiteReasonCodes.InvalidStudent));
    }
 
    // Negativan: nepoznata revizija se ne tumaci kao "nema preduslova".
    [Test]
    public async Task UnknownRevision_IsDenied()
    {
        var result = await _gate.CheckAsync(_ana, NewAssignment("rev-nepoznata"));
 
        Assert.That(result.IsUnlocked, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(PrerequisiteReasonCodes.LaboratoryUnresolved));
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