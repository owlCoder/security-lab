using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.ValueObjects;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Authoring.Application;
using Platform.Authoring.Domain;
using Platform.Authoring.Infrastructure;
using Platform.Authoring.Tests.Support;

namespace Platform.Authoring.Tests.Access;

/// <summary>Who may use the template actions: author directory, policy, in-use-case access check, wiring.</summary>
[TestFixture]
public class AuthoringAccessTests
{
    private static string CodeOf(AsyncTestDelegate act) => Assert.ThrowsAsync<DomainRuleException>(act)!.Code;

    // ---------- ConfigAuthorDirectory ----------

    [Test]
    public void Listed_account_is_an_author_and_an_unlisted_one_is_not()
    {
        var directory = new ConfigAuthorDirectory(TestHost.Config(TestHost.Author));

        Assert.That(directory.IsAuthor(TestHost.Author), Is.True);
        Assert.That(directory.IsAuthor(TestHost.Student), Is.False);
    }

    [Test]
    public void Missing_configuration_means_nobody_is_an_author()
    {
        var directory = new ConfigAuthorDirectory(new ConfigurationBuilder().Build());

        Assert.That(directory.IsAuthor(TestHost.Author), Is.False);
    }

    [TestCase("not-a-guid")]
    [TestCase("")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public void Invalid_or_empty_entries_never_grant_access(string raw)
    {
        var directory = new ConfigAuthorDirectory(TestHost.RawConfig(raw));

        Assert.That(directory.IsAuthor(Guid.Empty), Is.False);
        Assert.That(directory.IsAuthor(TestHost.Author), Is.False);
    }

    // ---------- AuthoringAuthorPolicy ----------

    private static AccessRequest Request(SubjectRef? subject) =>
        new(subject, AuthoringActions.TemplateCopy, null, "corr");

    [Test]
    public async Task Policy_allows_an_author()
    {
        var policy = new AuthoringAuthorPolicy(new ConfigAuthorDirectory(TestHost.Config(TestHost.Author)));

        Assert.That(await policy.EvaluateAsync(Request(TestHost.Active(TestHost.Author))), Is.True);
    }

    [Test]
    public async Task Policy_denies_a_non_author_a_missing_subject_and_unusable_ids()
    {
        var policy = new AuthoringAuthorPolicy(new ConfigAuthorDirectory(TestHost.Config(TestHost.Author)));

        Assert.That(await policy.EvaluateAsync(Request(TestHost.Active(TestHost.Student))), Is.False);
        Assert.That(await policy.EvaluateAsync(Request(null)), Is.False);
        Assert.That(await policy.EvaluateAsync(Request(new SubjectRef("not-a-guid", AccountState.Active))), Is.False);
        Assert.That(await policy.EvaluateAsync(Request(TestHost.Active(Guid.Empty))), Is.False);
    }

    // ---------- DirectoryAuthoringAccess (check inside the use cases) ----------

    [Test]
    public void Author_may_only_list_templates_and_copy_a_template()
    {
        var access = new DirectoryAuthoringAccess(new ConfigAuthorDirectory(TestHost.Config(TestHost.Author)));
        var anyId = Guid.NewGuid();

        Assert.That(access.IsAllowed(TestHost.Author, AuthoringAction.ListTemplates, null), Is.True);
        Assert.That(access.IsAllowed(TestHost.Author, AuthoringAction.CreateDraftFromTemplate, anyId), Is.True);

        // Not exposed yet: they stay denied until the draft endpoints bring their own ownership check.
        Assert.That(access.IsAllowed(TestHost.Author, AuthoringAction.CreateDraft, null), Is.False);
        Assert.That(access.IsAllowed(TestHost.Author, AuthoringAction.EditDraft, anyId), Is.False);
        Assert.That(access.IsAllowed(TestHost.Author, (AuthoringAction)99, null), Is.False);
    }

    [Test]
    public void Non_authors_and_the_empty_actor_are_denied_everything()
    {
        var access = new DirectoryAuthoringAccess(new ConfigAuthorDirectory(TestHost.Config(TestHost.Author)));

        foreach (var action in Enum.GetValues<AuthoringAction>())
        {
            Assert.That(access.IsAllowed(TestHost.Student, action, Guid.NewGuid()), Is.False);
            Assert.That(access.IsAllowed(Guid.Empty, action, Guid.NewGuid()), Is.False);
        }
    }

    // ---------- TemplateCatalogService ----------

    private static TemplateCatalogService Catalog() => new(
        new BuiltInTemplateRepository(),
        new DirectoryAuthoringAccess(new ConfigAuthorDirectory(TestHost.Config(TestHost.Author))));

    [Test]
    public async Task Author_can_list_all_built_in_templates()
    {
        var templates = await Catalog().ListAsync(TestHost.Author);

        Assert.That(templates.Count, Is.EqualTo(BuiltInTemplates.All.Count));
    }

    [Test]
    public void Listing_is_forbidden_for_a_non_author_and_for_the_empty_actor()
    {
        Assert.That(CodeOf(() => Catalog().ListAsync(TestHost.Student)), Is.EqualTo(AuthoringErrorCodes.Forbidden));
        Assert.That(CodeOf(() => Catalog().ListAsync(Guid.Empty)), Is.EqualTo(AuthoringErrorCodes.Forbidden));
    }

    // ---------- InMemoryDraftStore ----------

    private static LabDraft NewDraft() =>
        LabDraft.Create(Guid.NewGuid(), TestHost.Author, LabContent.Create("Lab", "Goal", LabType.AccessControl));

    [Test]
    public async Task Store_returns_a_copy_so_callers_cannot_change_stored_data_by_accident()
    {
        var store = new InMemoryDraftStore();
        var draft = NewDraft();
        await store.AddAsync(draft);

        var loaded = (await store.GetAsync(draft.Id))!;
        loaded.Edit(LabContent.Create("Changed in memory only", "", LabType.AccessControl), expectedVersion: 1);

        var again = (await store.GetAsync(draft.Id))!;
        Assert.That(ReferenceEquals(loaded, again), Is.False);
        Assert.That(again.Content.Title, Is.EqualTo("Lab"));
        Assert.That(again.Version, Is.EqualTo(1));
    }

    [Test]
    public async Task Store_rejects_a_duplicate_id_and_an_unknown_draft()
    {
        var store = new InMemoryDraftStore();
        var draft = NewDraft();
        await store.AddAsync(draft);

        Assert.That(CodeOf(() => store.AddAsync(draft)), Is.EqualTo(AuthoringErrorCodes.Conflict));
        Assert.That(await store.GetAsync(Guid.NewGuid()), Is.Null);
        Assert.That(CodeOf(() => store.SaveAsync(NewDraft(), previousVersion: 1)), Is.EqualTo(AuthoringErrorCodes.NotFound));
        Assert.That(store.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Store_accepts_a_save_only_from_the_version_that_is_stored()
    {
        var store = new InMemoryDraftStore();
        var draft = NewDraft();
        await store.AddAsync(draft);

        var first = (await store.GetAsync(draft.Id))!;
        var second = (await store.GetAsync(draft.Id))!;
        first.Edit(LabContent.Create("First writer", "", LabType.AccessControl), expectedVersion: 1);
        await store.SaveAsync(first, previousVersion: 1);

        second.Edit(LabContent.Create("Second writer", "", LabType.AccessControl), expectedVersion: 1);
        Assert.That(CodeOf(() => store.SaveAsync(second, previousVersion: 1)), Is.EqualTo(AuthoringErrorCodes.Conflict));
        Assert.That((await store.GetAsync(draft.Id))!.Content.Title, Is.EqualTo("First writer"));
    }

    // ---------- Composition ----------

    [Test]
    public void Module_registrations_are_valid_under_strict_scope_validation()
    {
        using var provider = TestHost.Build(TestHost.Author);

        Assert.That(provider.GetRequiredService<DraftService>(), Is.Not.Null);
        Assert.That(provider.GetRequiredService<TemplateCatalogService>(), Is.Not.Null);
    }

    [Test]
    public void Authoring_actions_are_valid_action_ids_and_registered_with_a_policy()
    {
        using var provider = TestHost.Build(TestHost.Author);
        var catalog = provider.GetRequiredService<IActionCatalog>();
        var policies = provider.GetRequiredService<IAccessPolicyRegistry>();

        foreach (var action in AuthoringActions.All)
        {
            Assert.That(ActionId.IsValid(action), Is.True);
            Assert.That(catalog.Contains(action), Is.True);
            Assert.That(policies.TryGet(action, out var policy) && policy is not null, Is.True);
        }

        // Deny by default: an action nobody registered is unknown to the catalog.
        Assert.That(catalog.Contains("authoring.draft.edit"), Is.False);
    }
}
