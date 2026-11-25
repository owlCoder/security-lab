using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Membership;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratoryFilterServiceTests
{
    private static readonly Caller Teacher = new("marko", PlatformRole.Teacher);
    private static readonly Caller OtherTeacher = new("jelena", PlatformRole.Teacher);
    private InMemoryLaboratoryRepository _repository = null!;
    private MetadataStore _metadata = null!;
    private LaboratorySearchService _service = null!;
    private InMemoryCourseMembership _membership = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new();
        _metadata = new();
        _membership = LabSearchTestContext.CreateMembership();
        _service = new(_repository, _membership, new FakeAccessDecision(),
            NullLogger<LaboratorySearchService>.Instance, _metadata);
    }

    [Test]
    public async Task Search_CombinesFiltersAndRemovingSelectionsBroadensResults()
    {
        Laboratory first = await AddAsync("Lab A", LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, ["security", "access"]);
        Laboratory second = await AddAsync("Lab B", LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, ["security"]);
        Laboratory third = await AddAsync("Lab C", LaboratoryDifficulty.Easy, LaboratoryType.AccessControl, ["security"]);
        Laboratory fourth = await AddAsync("Lab D", LaboratoryDifficulty.Hard, LaboratoryType.LogAnalysis, ["security", "access"]);
        Laboratory missing = await AddAsync("Lab without metadata");
        Laboratory hidden = await AddAsync("Lab hidden", LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, ["security", "access"], OtherTeacher);
        var filters = new LabSearchFilters(LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, ["security", "access"]);

        LabSearchResult combined = await SearchAsync("lab", filters);
        LabSearchResult oneTag = await SearchAsync("lab", filters with { Tags = ["security"] });
        LabSearchResult noType = await SearchAsync("lab", filters with { Type = null, Tags = ["security"] });
        LabSearchResult reset = await SearchAsync(null, new());

        Assert.Multiple(() =>
        {
            Assert.That(combined.Items.Select(l => l.Id), Is.EqualTo(new[] { first.Id }));
            Assert.That(oneTag.Items.Select(l => l.Id), Is.EquivalentTo(new[] { first.Id, second.Id }));
            Assert.That(noType.Items.Select(l => l.Id), Is.EquivalentTo(new[] { first.Id, second.Id, third.Id }));
            Assert.That(reset.Items.Select(l => l.Id), Is.EquivalentTo(new[] { first.Id, second.Id, third.Id, fourth.Id, missing.Id }));
            Assert.That(_metadata.Reads, Does.Not.Contain(hidden.Id));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task Search_EmptyTitleAllowsFiltersOnlyAndReset(string? query)
    {
        Laboratory easy = await AddAsync("First", LaboratoryDifficulty.Easy);
        Laboratory hard = await AddAsync("Second", LaboratoryDifficulty.Hard);

        LabSearchResult filtered = await SearchAsync(query, new(LaboratoryDifficulty.Easy));
        LabSearchResult reset = await SearchAsync(query, new());

        Assert.Multiple(() =>
        {
            Assert.That(filtered.Status, Is.EqualTo(LabSearchStatus.Ok));
            Assert.That(filtered.Items.Select(l => l.Id), Is.EqualTo(new[] { easy.Id }));
            Assert.That(reset.Items.Select(l => l.Id), Is.EquivalentTo(new[] { easy.Id, hard.Id }));
        });
    }

    [Test]
    public async Task Search_TagsAreTrimmedCaseInsensitiveExactMatchesAndAllAreRequired()
    {
        Laboratory match = await AddAsync("Lab exact", LaboratoryDifficulty.Easy, tags: ["security", "access"]);
        await AddAsync("Lab partial", LaboratoryDifficulty.Easy, tags: ["security-tools", "access"]);
        await AddAsync("Lab one tag", LaboratoryDifficulty.Easy, tags: ["security"]);

        LabSearchResult result = await SearchAsync(null, new(Tags: [" SECURITY ", "Access", "security"]));

        Assert.That(result.Items.Select(l => l.Id), Is.EqualTo(new[] { match.Id }));
    }

    [Test]
    public async Task Search_FilteringPrecedesResultLimitAndTruncation()
    {
        for (int i = 0; i < LaboratorySearchService.MaxResults + 10; i++)
            await AddAsync($"Lab {i:D3}", LaboratoryDifficulty.Hard);
        Laboratory selected = await AddAsync("Lab Z selected", LaboratoryDifficulty.Easy);

        LabSearchResult result = await SearchAsync("lab", new(LaboratoryDifficulty.Easy));

        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Select(l => l.Id), Is.EqualTo(new[] { selected.Id }));
            Assert.That(result.Truncated, Is.False);
        });
    }

    [Test]
    public async Task Options_OnlyReadAndExposeMetadataOfVisibleLaboratories()
    {
        Laboratory own = await AddAsync("Own", LaboratoryDifficulty.Easy, tags: ["visible", "shared"]);
        Laboratory hidden = await AddAsync("Hidden", LaboratoryDifficulty.Easy, tags: ["secret", "shared"], owner: OtherTeacher);
        var options = await _service.GetFilterOptionsAsync(Teacher, CancellationToken.None);
        var studentOptions = await _service.GetFilterOptionsAsync(new Caller("marko", PlatformRole.Student), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(options.Difficulties, Is.EqualTo(Enum.GetNames<LaboratoryDifficulty>()));
            Assert.That(options.Types, Is.EqualTo(Enum.GetNames<LaboratoryType>()));
            Assert.That(options.Tags, Is.EqualTo(new[] { "shared", "visible" }));
            Assert.That(options.MetadataAvailable, Is.True);
            Assert.That(studentOptions.Tags, Is.Empty);
            Assert.That(_metadata.Reads, Is.EqualTo(new[] { own.Id }));
            Assert.That(_metadata.Reads, Does.Not.Contain(hidden.Id));
        });
    }

    [Test]
    public async Task MissingStore_DoesNotDisableTitleSearchOrSilentlyIgnoreFilters()
    {
        Laboratory lab = await AddAsync("Lab", LaboratoryDifficulty.Easy);
        var service = new LaboratorySearchService(_repository, _membership, new FakeAccessDecision(), NullLogger<LaboratorySearchService>.Instance);

        LabSearchResult title = await service.SearchAsync("lab", Teacher, CancellationToken.None);
        LabSearchResult filtered = await service.SearchAsync(null, Teacher, CancellationToken.None, new(LaboratoryDifficulty.Easy));
        LabSearchFilterOptions options = await service.GetFilterOptionsAsync(Teacher, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(title.Items.Select(l => l.Id), Is.EqualTo(new[] { lab.Id }));
            Assert.That(filtered.Status, Is.EqualTo(LabSearchStatus.FiltersUnavailable));
            Assert.That(filtered.Items, Is.Empty);
            Assert.That(options.MetadataAvailable, Is.False);
            Assert.That(options.Tags, Is.Empty);
        });
    }

    [Test]
    public async Task Search_InvalidFiltersAreRejectedBeforeMetadataReads()
    {
        LabSearchFilters[] invalid =
        [
            new((LaboratoryDifficulty)999), new(Type: (LaboratoryType)999),
            new(Tags: [""]), new(Tags: ["bad\nvalue"]),
        ];
        foreach (var filters in invalid)
            Assert.That((await SearchAsync(null, filters)).Status, Is.EqualTo(LabSearchStatus.InvalidQuery));
        Assert.That(_metadata.Reads, Is.Empty);
    }

    [Test]
    public async Task Search_UsesExistingTagsWithoutIntroducingMetadataLengthOrCountRestrictions()
    {
        string[] tags = Enumerable.Range(0, 21).Select(i => new string('a', 201) + i).ToArray();
        Laboratory laboratory = await AddAsync("Lab", LaboratoryDifficulty.Easy, tags: tags);

        LabSearchResult result = await SearchAsync(null, new(Tags: tags));
        LabSearchFilterOptions options = await _service.GetFilterOptionsAsync(Teacher, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Select(lab => lab.Id), Is.EqualTo(new[] { laboratory.Id }));
            Assert.That(options.Tags, Is.EquivalentTo(tags));
        });
    }

    [Test]
    public async Task Search_StoreFailureReturnsNoPartialResultsAndDoesNotReadHiddenMetadata()
    {
        await AddAsync("First", LaboratoryDifficulty.Easy);
        Laboratory failing = await AddAsync("Second", LaboratoryDifficulty.Easy);
        _metadata.FailOn = failing.Id;

        Assert.ThrowsAsync<InvalidOperationException>(() => SearchAsync(null, new(LaboratoryDifficulty.Easy)));
        Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetFilterOptionsAsync(Teacher, CancellationToken.None));
    }

    [Test]
    public async Task Search_AnonymousCallerCannotReadMetadataOrOptions()
    {
        await AddAsync("Lab", LaboratoryDifficulty.Easy);
        var caller = new AnonymousIdentityContext();
        var result = await _service.SearchAsync(null, caller, CancellationToken.None, new(LaboratoryDifficulty.Easy));
        var options = await _service.GetFilterOptionsAsync(caller, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LabSearchStatus.Unauthenticated));
            Assert.That(options.Status, Is.EqualTo(LabSearchStatus.Unauthenticated));
            Assert.That(_metadata.Reads, Is.Empty);
        });
    }

    [TestCase(CourseRole.None, false)]
    [TestCase(CourseRole.Teacher, true)]
    public async Task SearchAndOptions_UseMembershipOfStoredCourse(CourseRole role, bool allowed)
    {
        Laboratory lab = await AddAsync("Lab member", LaboratoryDifficulty.Easy, tags: ["course-secret"]);
        Guid callerId = CallerId.From(Teacher)!.Value;
        _membership.Set(Guid.NewGuid(), callerId, CourseRole.Teacher);
        _membership.Set(lab.CourseId, callerId, role);

        var result = await SearchAsync("lab", new(LaboratoryDifficulty.Easy));
        var options = await _service.GetFilterOptionsAsync(Teacher, CancellationToken.None);
        var reader = new LaboratoryService(_repository, _membership, TimeProvider.System,
            NullLogger<LaboratoryService>.Instance);
        var opened = await reader.GetAsync(lab.Id, Teacher, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LabSearchStatus.Ok));
            Assert.That(result.Items.Count, Is.EqualTo(allowed ? 1 : 0));
            Assert.That(options.Tags, Is.EqualTo(allowed ? new[] { "course-secret" } : Array.Empty<string>()));
            Assert.That(opened.Status, Is.EqualTo(allowed ? LabResultStatus.Ok : LabResultStatus.NotFound));
            Assert.That(_metadata.Reads.Count, Is.EqualTo(allowed ? 2 : 0));
        });
    }

    [Test]
    public async Task RevokedMembership_HidesResultsAndTagsForTitleFiltersAndReset()
    {
        await AddAsync("Lab member", LaboratoryDifficulty.Easy, tags: ["course-secret"]);
        Assert.That((await SearchAsync("lab", new())).Items, Has.Count.EqualTo(1));
        Assert.That((await _service.GetFilterOptionsAsync(Teacher, CancellationToken.None)).Tags,
            Does.Contain("course-secret"));

        _membership.Set(LabSearchTestContext.CourseId, CallerId.From(Teacher)!.Value, CourseRole.None);
        _metadata.Reads.Clear();
        foreach (var criteria in new (string? Query, LabSearchFilters Filters)[]
                 { ("lab", new()), (null, new(LaboratoryDifficulty.Easy)), (null, new()) })
        {
            var result = await SearchAsync(criteria.Query, criteria.Filters);
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.Truncated, Is.False);
        }
        Assert.That((await _service.GetFilterOptionsAsync(Teacher, CancellationToken.None)).Tags, Is.Empty);
        Assert.That(_metadata.Reads, Is.Empty);
    }

    [Test]
    public async Task LegacyLaboratoryWithoutCourse_IsNotReturnedOrReadForTags()
    {
        var legacy = Laboratory.Rehydrate(Guid.NewGuid(), "Lab legacy", LabStatus.Draft,
            CallerId.From(Teacher)!.Value, DateTimeOffset.UtcNow);
        var service = new LaboratorySearchService(new LegacyRepository(legacy), _membership,
            new FakeAccessDecision(), NullLogger<LaboratorySearchService>.Instance, _metadata);
        _metadata.Items[legacy.Id] = new(legacy.Id, "Legacy objective", LaboratoryDifficulty.Easy,
            LaboratoryType.LogAnalysis, ["legacy-secret"]);

        Assert.That((await service.SearchAsync("lab", Teacher, CancellationToken.None, new(LaboratoryDifficulty.Easy))).Items, Is.Empty);
        Assert.That((await service.GetFilterOptionsAsync(Teacher, CancellationToken.None)).Tags, Is.Empty);
        Assert.That(_metadata.Reads, Is.Empty);
    }

    private sealed class LegacyRepository(Laboratory legacy) : ILaboratoryRepository
    {
        public Task<IReadOnlyList<Laboratory>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Laboratory>>([legacy]);
        public Task AddAsync(Laboratory laboratory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Laboratory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Laboratory laboratory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LabRevision> AddRevisionAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LabRevision>> GetRevisionsAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Test]
    public async Task MembershipFailure_StopsSearchAndOptionsWithoutReadingMetadata()
    {
        await AddAsync("Lab member", LaboratoryDifficulty.Easy);
        var service = new LaboratorySearchService(_repository, new UnavailableMembership(),
            new FakeAccessDecision(), NullLogger<LaboratorySearchService>.Instance, _metadata);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SearchAsync("lab", Teacher, CancellationToken.None, new(LaboratoryDifficulty.Easy)));
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetFilterOptionsAsync(Teacher, CancellationToken.None));
        Assert.That(_metadata.Reads, Is.Empty);
    }

    private sealed class UnavailableMembership : ICourseMembership
    {
        public ValueTask<CourseRole> GetRoleAsync(Guid courseId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Membership unavailable.");
    }

    private Task<LabSearchResult> SearchAsync(string? query, LabSearchFilters filters) =>
        _service.SearchAsync(query, Teacher, CancellationToken.None, filters);

    private async Task<Laboratory> AddAsync(string title, LaboratoryDifficulty? difficulty = null,
        LaboratoryType type = LaboratoryType.LogAnalysis, string[]? tags = null, Caller? owner = null)
    {
        var lab = new Laboratory(LabSearchTestContext.CourseId, title, CallerId.From(owner ?? Teacher)!.Value);
        await _repository.AddAsync(lab);
        if (difficulty is not null)
            _metadata.Items[lab.Id] = new(lab.Id, "Existing LAB-02 objective", difficulty.Value, type, tags ?? []);
        return lab;
    }

    private sealed record Caller(string? UserId, PlatformRole? Role) : IIdentityContext
    {
        public bool IsAuthenticated => true;
    }

    private sealed class MetadataStore : ILaboratoryMetadataStore
    {
        public Dictionary<Guid, LaboratoryMetadata> Items { get; } = [];
        public List<Guid> Reads { get; } = [];
        public Guid? FailOn { get; set; }

        public Task<LaboratoryMetadata?> GetAsync(Guid laboratoryId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Reads.Add(laboratoryId);
            if (FailOn == laboratoryId) throw new InvalidOperationException("Metadata unavailable.");
            return Task.FromResult(Items.GetValueOrDefault(laboratoryId));
        }

        public Task SaveAsync(LaboratoryMetadata metadata, CancellationToken ct = default) =>
            throw new NotSupportedException("Laboratory search must never write metadata.");
    }
}
