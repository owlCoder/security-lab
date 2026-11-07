using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratorySearchServiceTests
{
    private static readonly Caller Marko = new("marko", PlatformRole.Teacher);
    private static readonly Caller Jelena = new("jelena", PlatformRole.Teacher);
    private static readonly Caller Ana = new("ana", PlatformRole.Student);

    private InMemoryLaboratoryRepository _repository = null!;
    private LaboratorySearchService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new InMemoryLaboratoryRepository();
        _service = CreateService(new FakeAccessDecision());
    }

    [TestCase("RBAC i kontrola pristupa")]
    [TestCase("kontrola")]
    [TestCase("KONTROLA PRISTUPA")]
    [TestCase("  pristupa  ")]
    [TestCase("i kontrola")]
    public async Task Search_ByFullOrPartialTitle_FindsOwnLaboratory(string query)
    {
        Laboratory lab = await AddAsync(Marko, "RBAC i kontrola pristupa");
        await AddAsync(Marko, "Analiza logova");

        LabSearchResult result = await _service.SearchAsync(query, Marko, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabSearchStatus.Ok));
        Assert.That(result.Items.Select(l => l.Id), Is.EqualTo(new[] { lab.Id }));
        Assert.That(result.Truncated, Is.False);
    }

    [TestCase("sifr")]
    [TestCase("ŠIFR")]
    [TestCase("kljuc")]
    [TestCase("ključ")]
    public async Task Search_IgnoresDiacritics(string query)
    {
        Laboratory lab = await AddAsync(Marko, "Šifrovanje i ključevi");

        LabSearchResult result = await _service.SearchAsync(query, Marko, CancellationToken.None);

        Assert.That(result.Items.Select(l => l.Id), Is.EqualTo(new[] { lab.Id }));
    }

    [Test]
    public async Task Search_WhenNothingMatches_ReturnsEmptyList()
    {
        await AddAsync(Marko, "Analiza logova");

        LabSearchResult result = await _service.SearchAsync("firewall", Marko, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabSearchStatus.Ok));
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task Search_ListsTitlesStartingWithQueryFirstThenAlphabetically()
    {
        await AddAsync(Marko, "Uvod u RBAC");
        await AddAsync(Marko, "RBAC napredno");
        await AddAsync(Marko, "Analiza rbac logova");
        await AddAsync(Marko, "rbac osnove");

        LabSearchResult result = await _service.SearchAsync("rbac", Marko, CancellationToken.None);

        Assert.That(result.Items.Select(l => l.Title), Is.EqualTo(new[]
        {
            "RBAC napredno", "rbac osnove", "Analiza rbac logova", "Uvod u RBAC",
        }));
    }

    [Test]
    public async Task Search_ReturnsAtMostMaxResultsAndMarksTruncated()
    {
        for (int i = 0; i < LaboratorySearchService.MaxResults + 1; i++)
        {
            await AddAsync(Marko, $"Lab {i:D3}");
        }

        LabSearchResult result = await _service.SearchAsync("lab", Marko, CancellationToken.None);

        Assert.That(result.Items, Has.Count.EqualTo(LaboratorySearchService.MaxResults));
        Assert.That(result.Truncated, Is.True);
    }

    [Test]
    public async Task Search_WithExactlyMaxResults_IsNotTruncated()
    {
        for (int i = 0; i < LaboratorySearchService.MaxResults; i++)
        {
            await AddAsync(Marko, $"Lab {i:D3}");
        }

        LabSearchResult result = await _service.SearchAsync("lab", Marko, CancellationToken.None);

        Assert.That(result.Items, Has.Count.EqualTo(LaboratorySearchService.MaxResults));
        Assert.That(result.Truncated, Is.False);
    }

    [Test]
    public async Task Search_DoesNotReturnLaboratoriesOfAnotherTeacher()
    {
        Laboratory own = await AddAsync(Jelena, "RBAC - Jelenin nacrt");
        await AddAsync(Marko, "RBAC - Markov nacrt");

        LabSearchResult result = await _service.SearchAsync("rbac", Jelena, CancellationToken.None);

        Assert.That(result.Items.Select(l => l.Id), Is.EqualTo(new[] { own.Id }));
    }

    [Test]
    public async Task Search_WhenStudentSearches_DoesNotSeeDrafts()
    {
        await AddAsync(Marko, "RBAC i kontrola pristupa");

        LabSearchResult result = await _service.SearchAsync("rbac", Ana, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabSearchStatus.Ok));
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task Search_WhenSameUserHasStudentRole_DoesNotSeeOwnDraft()
    {
        await AddAsync(Marko, "RBAC i kontrola pristupa");

        LabSearchResult result = await _service.SearchAsync("rbac", new Caller("marko", PlatformRole.Student), CancellationToken.None);

        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task Search_ChecksReadPermissionForEachLaboratory()
    {
        Laboratory allowed = await AddAsync(Marko, "RBAC dozvoljena");
        Laboratory denied = await AddAsync(Marko, "RBAC zabranjena");
        var decision = new DenyReadOfOneLaboratory(denied.Id);
        LaboratorySearchService service = CreateService(decision);

        LabSearchResult result = await service.SearchAsync("rbac", Marko, CancellationToken.None);

        Assert.That(result.Items.Select(l => l.Id), Is.EqualTo(new[] { allowed.Id }));
        Assert.That(decision.CheckedReads, Is.EquivalentTo(new[] { allowed.Id.ToString(), denied.Id.ToString() }));
    }

    [Test]
    public async Task Search_WhenAccessDecisionDeniesEverything_ReturnsNothing()
    {
        await AddAsync(Marko, "RBAC i kontrola pristupa");
        LaboratorySearchService service = CreateService(new DenyAllAccessDecision());

        LabSearchResult result = await service.SearchAsync("rbac", Marko, CancellationToken.None);

        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task Search_WhenCallerIsAnonymous_ReturnsUnauthenticated()
    {
        await AddAsync(Marko, "RBAC i kontrola pristupa");

        LabSearchResult result = await _service.SearchAsync("rbac", new AnonymousIdentityContext(), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabSearchStatus.Unauthenticated));
        Assert.That(result.Items, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("rbac\n")]
    public async Task Search_WithInvalidQuery_ReturnsInvalidQuery(string? query)
    {
        await AddAsync(Marko, "RBAC i kontrola pristupa");

        LabSearchResult result = await _service.SearchAsync(query, Marko, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabSearchStatus.InvalidQuery));
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task Search_WithOverlongQuery_ReturnsInvalidQuery()
    {
        LabSearchResult result = await _service.SearchAsync(new string('a', Laboratory.MaxTitleLength + 1), Marko, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabSearchStatus.InvalidQuery));
    }

    [TestCase("' OR '1'='1")]
    [TestCase("%")]
    [TestCase("*")]
    public async Task Search_WithWildcardOrInjectionLikeText_TreatsItAsPlainText(string query)
    {
        await AddAsync(Marko, "RBAC i kontrola pristupa");
        await AddAsync(Marko, "Analiza logova");

        LabSearchResult result = await _service.SearchAsync(query, Marko, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabSearchStatus.Ok));
        Assert.That(result.Items, Is.Empty);
    }

    [TestCase(PlatformRole.Student)]
    [TestCase(PlatformRole.Teacher)]
    [TestCase(PlatformRole.Assistant)]
    [TestCase(PlatformRole.Administrator)]
    public void SearchAction_IsAllowedForEveryKnownRole(PlatformRole role)
    {
        AccessResult result = new FakeAccessDecision().Decide(new Caller("korisnik", role), LabActions.Search);

        Assert.That(result.Allowed, Is.True);
    }

    [Test]
    public void SearchAction_WhenUserIsUnknown_IsDenied()
    {
        AccessResult result = new FakeAccessDecision().Decide(new AnonymousIdentityContext(), LabActions.Search);

        Assert.That(result.Allowed, Is.False);
        Assert.That(result.Reason, Is.EqualTo(DenyReason.Unauthenticated));
    }

    [TestCase(PlatformRole.Assistant)]
    [TestCase(PlatformRole.Administrator)]
    public async Task Search_WhenAssistantOrAdministratorSearches_DoesNotSeeDrafts(PlatformRole role)
    {
        await AddAsync(Marko, "RBAC i kontrola pristupa");

        LabSearchResult otherUser = await _service.SearchAsync("rbac", new Caller("petar", role), CancellationToken.None);
        LabSearchResult sameUser = await _service.SearchAsync("rbac", new Caller("marko", role), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(otherUser.Status, Is.EqualTo(LabSearchStatus.Ok));
            Assert.That(otherUser.Items, Is.Empty);
            Assert.That(sameUser.Items, Is.Empty);
        });
    }

    [Test]
    public async Task Search_RechecksAccessOnEveryRequest()
    {
        Laboratory lab = await AddAsync(Marko, "RBAC i kontrola pristupa");
        var decision = new RevocableReadAccess();
        LaboratorySearchService service = CreateService(decision);

        LabSearchResult before = await service.SearchAsync("rbac", Marko, CancellationToken.None);
        decision.Revoke(lab.Id);
        LabSearchResult after = await service.SearchAsync("rbac", Marko, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(before.Items.Select(l => l.Id), Is.EqualTo(new[] { lab.Id }));
            Assert.That(after.Items, Is.Empty);
        });
    }

    [Test]
    public async Task Search_HiddenLaboratoriesDoNotAffectLimitOrTruncation()
    {
        for (int i = 0; i < LaboratorySearchService.MaxResults; i++)
        {
            await AddAsync(Marko, $"Lab {i:D3}");
        }

        for (int i = 0; i < 10; i++)
        {
            await AddAsync(Jelena, $"Lab tudji {i:D3}");
        }

        LabSearchResult result = await _service.SearchAsync("lab", Marko, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(LaboratorySearchService.MaxResults));
            Assert.That(result.Truncated, Is.False);
            Assert.That(result.Items.Select(l => l.CreatedBy).Distinct(), Is.EqualTo(new[] { CallerId.From(Marko) }));
        });
    }

    [Test]
    public async Task Search_WhenAccessCheckFails_ReturnsNoPartialResults()
    {
        await AddAsync(Marko, "RBAC prva");
        Laboratory failing = await AddAsync(Marko, "RBAC druga");
        LaboratorySearchService service = CreateService(new ReadCheckFailsFor(failing.Id));

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SearchAsync("rbac", Marko, CancellationToken.None));
    }

    private LaboratorySearchService CreateService(IAccessDecision decision) =>
        new(_repository, decision, NullLogger<LaboratorySearchService>.Instance);

    private async Task<Laboratory> AddAsync(Caller owner, string title)
    {
        var laboratory = new Laboratory(title, CallerId.From(owner)!.Value);
        await _repository.AddAsync(laboratory);
        return laboratory;
    }

    private sealed class Caller(string userId, PlatformRole role) : IIdentityContext
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; } = userId;
        public PlatformRole? Role { get; } = role;
    }

    private sealed class DenyReadOfOneLaboratory(Guid deniedId) : IAccessDecision
    {
        private readonly FakeAccessDecision _inner = new();

        public List<string> CheckedReads { get; } = [];

        public AccessResult Decide(IIdentityContext subject, string action, string? resourceId = null)
        {
            if (action == LabActions.Read && resourceId is not null)
            {
                CheckedReads.Add(resourceId);
                if (resourceId == deniedId.ToString())
                {
                    return AccessResult.Deny(DenyReason.NotPermitted);
                }
            }

            return _inner.Decide(subject, action, resourceId);
        }
    }

    private sealed class RevocableReadAccess : IAccessDecision
    {
        private readonly FakeAccessDecision _inner = new();
        private readonly HashSet<string> _revoked = [];

        public void Revoke(Guid laboratoryId) => _revoked.Add(laboratoryId.ToString());

        public AccessResult Decide(IIdentityContext subject, string action, string? resourceId = null) =>
            action == LabActions.Read && resourceId is not null && _revoked.Contains(resourceId)
                ? AccessResult.Deny(DenyReason.NotPermitted)
                : _inner.Decide(subject, action, resourceId);
    }

    private sealed class ReadCheckFailsFor(Guid laboratoryId) : IAccessDecision
    {
        private readonly FakeAccessDecision _inner = new();

        public AccessResult Decide(IIdentityContext subject, string action, string? resourceId = null) =>
            action == LabActions.Read && resourceId == laboratoryId.ToString()
                ? throw new InvalidOperationException("Access store unavailable.")
                : _inner.Decide(subject, action, resourceId);
    }
}
