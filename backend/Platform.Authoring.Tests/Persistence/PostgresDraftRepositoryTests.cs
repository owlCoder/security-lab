using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Authoring.Application;
using Platform.Authoring.Composition;
using Platform.Authoring.Domain;
using Platform.Authoring.Infrastructure;
using Platform.Authoring.Infrastructure.Persistence;

namespace Platform.Authoring.Tests.Persistence;

/// <summary>
/// AUT-01 / AUT-06 storage tests against a real PostgreSQL. They are skipped unless
/// AUTHORING_TEST_CONNECTION_STRING is set (same convention as the Audit tests), so CI without a database stays green.
/// Each test uses fresh random ids, so the tests need no cleanup and can share one database.
/// </summary>
[TestFixture]
[Category("AuthoringPostgres")]
public sealed class PostgresDraftRepositoryTests
{
    private string _connectionString = null!;
    private readonly Guid _owner = Guid.NewGuid();

    [OneTimeSetUp]
    public async Task MigrateDatabase()
    {
        var configured = Environment.GetEnvironmentVariable("AUTHORING_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set AUTHORING_TEST_CONNECTION_STRING to run the Authoring storage tests against PostgreSQL.");
            return;
        }

        _connectionString = new NpgsqlConnectionStringBuilder(configured) { Pooling = false }.ConnectionString;
        await AuthoringPersistenceExtensions.MigrateAuthoringDatabaseAsync(_connectionString);
    }

    // A new context per operation, like one per HTTP request: nothing is served from a cache.
    private AuthoringDbContext OpenContext() => new(new DbContextOptionsBuilder<AuthoringDbContext>()
        .UseNpgsql(_connectionString, p => p.MigrationsHistoryTable("__EFMigrationsHistory", AuthoringDbContext.Schema))
        .Options);

    private static LabContent Content(string title = "Lab one", string goal = "Goal") =>
        LabContent.Create(title, goal, LabType.AccessControl);

    private async Task<LabDraft> AddNewDraftAsync(LabContent? content = null, Guid? source = null)
    {
        var draft = LabDraft.Create(Guid.NewGuid(), _owner, content ?? Content(), source);
        await using var db = OpenContext();
        await new PostgresDraftRepository(db).AddAsync(draft);
        return draft;
    }

    private async Task<LabDraft?> ReadAsync(Guid id)
    {
        await using var db = OpenContext();
        return await new PostgresDraftRepository(db).GetAsync(id);
    }

    [Test]
    public async Task A_stored_draft_is_read_back_unchanged_by_a_new_context()
    {
        var source = BuiltInTemplates.LogAnalysisId;
        var draft = await AddNewDraftAsync(LabContent.Create("Stored lab", "Line one\nLine two", LabType.LogAnalysis), source);

        var read = await ReadAsync(draft.Id);

        Assert.That(read, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read!.OwnerId, Is.EqualTo(_owner));
            Assert.That(read.SourceTemplateId, Is.EqualTo(source));
            Assert.That(read.Status, Is.EqualTo(DraftStatus.Draft));
            Assert.That(read.Version, Is.EqualTo(1));
            Assert.That(read.Content, Is.EqualTo(draft.Content));
        }
    }

    [Test]
    public async Task An_unknown_draft_is_null()
    {
        Assert.That(await ReadAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task Adding_an_existing_id_is_CONFLICT_and_keeps_the_stored_draft()
    {
        var draft = await AddNewDraftAsync(Content("Original"));
        var duplicate = LabDraft.Create(draft.Id, Guid.NewGuid(), Content("Overwrite attempt"));

        await using var db = OpenContext();
        var ex = Assert.ThrowsAsync<DomainRuleException>(() => new PostgresDraftRepository(db).AddAsync(duplicate));

        Assert.That(ex!.Code, Is.EqualTo(AuthoringErrorCodes.Conflict));
        var read = await ReadAsync(draft.Id);
        Assert.That(read!.Content.Title, Is.EqualTo("Original"));
        Assert.That(read.OwnerId, Is.EqualTo(_owner));
    }

    [Test]
    public async Task Saving_with_the_current_version_stores_the_change_and_bumps_the_version()
    {
        var draft = await AddNewDraftAsync();
        var loaded = (await ReadAsync(draft.Id))!;
        Assert.That(loaded.Edit(Content("Edited", "New goal"), expectedVersion: 1), Is.True);

        await using (var db = OpenContext())
            await new PostgresDraftRepository(db).SaveAsync(loaded, previousVersion: 1);

        var read = (await ReadAsync(draft.Id))!;
        Assert.That(read.Version, Is.EqualTo(2));
        Assert.That(read.Content.Title, Is.EqualTo("Edited"));
        Assert.That(read.Content.Goal, Is.EqualTo("New goal"));
    }

    [Test]
    public async Task A_stale_save_is_CONFLICT_and_does_not_overwrite_newer_work()
    {
        var draft = await AddNewDraftAsync();
        var first = (await ReadAsync(draft.Id))!;
        var second = (await ReadAsync(draft.Id))!;
        first.Edit(Content("First editor"), 1);
        second.Edit(Content("Second editor"), 1);

        await using (var db = OpenContext())
            await new PostgresDraftRepository(db).SaveAsync(first, 1);

        await using var db2 = OpenContext();
        var ex = Assert.ThrowsAsync<DomainRuleException>(() => new PostgresDraftRepository(db2).SaveAsync(second, 1));

        Assert.That(ex!.Code, Is.EqualTo(AuthoringErrorCodes.Conflict));
        Assert.That((await ReadAsync(draft.Id))!.Content.Title, Is.EqualTo("First editor"));
    }

    [Test]
    public async Task Two_simultaneous_saves_of_the_same_version_let_exactly_one_win()
    {
        var draft = await AddNewDraftAsync();
        var a = (await ReadAsync(draft.Id))!;
        var b = (await ReadAsync(draft.Id))!;
        a.Edit(Content("Writer A"), 1);
        b.Edit(Content("Writer B"), 1);

        async Task<bool> TrySave(LabDraft d)
        {
            await using var db = OpenContext();
            try { await new PostgresDraftRepository(db).SaveAsync(d, 1); return true; }
            catch (DomainRuleException ex) when (ex.Code == AuthoringErrorCodes.Conflict) { return false; }
        }

        var results = await Task.WhenAll(TrySave(a), TrySave(b));

        Assert.That(results.Count(won => won), Is.EqualTo(1));
        Assert.That((await ReadAsync(draft.Id))!.Version, Is.EqualTo(2));
    }

    [Test]
    public async Task Saving_a_draft_that_does_not_exist_is_NOT_FOUND_and_stores_nothing()
    {
        var ghost = LabDraft.Create(Guid.NewGuid(), _owner, Content());
        ghost.Edit(Content("Changed"), 1);

        await using var db = OpenContext();
        var ex = Assert.ThrowsAsync<DomainRuleException>(() => new PostgresDraftRepository(db).SaveAsync(ghost, 1));

        Assert.That(ex!.Code, Is.EqualTo(AuthoringErrorCodes.NotFound));
        Assert.That(await ReadAsync(ghost.Id), Is.Null);
    }

    [Test]
    public async Task Copying_a_template_stores_a_new_owned_draft_and_leaves_the_template_unchanged()
    {
        var templates = new BuiltInTemplateRepository();
        var before = await templates.GetAsync(BuiltInTemplates.AccessControlId);

        LabDraft copy;
        await using (var db = OpenContext())
        {
            var service = new DraftService(new PostgresDraftRepository(db), templates, new AllowEveryone());
            copy = await service.CreateDraftFromTemplateAsync(_owner, BuiltInTemplates.AccessControlId);
        }

        var read = (await ReadAsync(copy.Id))!;
        var after = await templates.GetAsync(BuiltInTemplates.AccessControlId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(copy.Id, Is.Not.EqualTo(BuiltInTemplates.AccessControlId));
            Assert.That(read.OwnerId, Is.EqualTo(_owner));
            Assert.That(read.SourceTemplateId, Is.EqualTo(BuiltInTemplates.AccessControlId));
            Assert.That(read.Content, Is.EqualTo(before!.Content));
            Assert.That(after, Is.SameAs(before));
        }
    }

    [Test]
    public async Task A_denied_copy_stores_nothing()
    {
        var rowsBefore = await CountRowsAsync();

        await using (var db = OpenContext())
        {
            var service = new DraftService(new PostgresDraftRepository(db), new BuiltInTemplateRepository(), new DenyEveryone());
            var ex = Assert.ThrowsAsync<DomainRuleException>(
                () => service.CreateDraftFromTemplateAsync(Guid.NewGuid(), BuiltInTemplates.AccessControlId));
            Assert.That(ex!.Code, Is.EqualTo(AuthoringErrorCodes.Forbidden));
        }

        Assert.That(await CountRowsAsync(), Is.EqualTo(rowsBefore));
    }

    private async Task<int> CountRowsAsync()
    {
        await using var db = OpenContext();
        return await db.Drafts.CountAsync();
    }

    private sealed class AllowEveryone : IAuthoringAccess
    {
        public bool IsAllowed(Guid actorId, AuthoringAction action, Guid? resourceId) => actorId != Guid.Empty;
    }

    private sealed class DenyEveryone : IAuthoringAccess
    {
        public bool IsAllowed(Guid actorId, AuthoringAction action, Guid? resourceId) => false;
    }
}
