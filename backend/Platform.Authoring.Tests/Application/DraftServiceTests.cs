using Platform.Authoring.Application;
using Platform.Authoring.Domain;
using Platform.Authoring.Infrastructure;
using Platform.Authoring.Tests.Fakes;

namespace Platform.Authoring.Tests.Application;

/// <summary>
/// Use-case level tests for AUT-01 and AUT-06, including the negative access paths
/// (a student or another teacher must never create, read the existence of, or change a draft).
/// </summary>
[TestFixture]
public class DraftServiceTests
{
    private readonly Guid _author = Guid.NewGuid();
    private readonly Guid _otherAuthor = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();

    private InMemoryDraftRepository _drafts = null!;
    private DraftService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _drafts = new InMemoryDraftRepository();
        _service = new DraftService(_drafts, new BuiltInTemplateRepository(),
            new FakeAuthoringAccess(_drafts, _author, _otherAuthor));
    }

    private static LabContent Content(string title = "Lab one") => LabContent.Create(title, "Goal", LabType.AccessControl);

    private static string CodeOf(AsyncTestDelegate act) => Assert.ThrowsAsync<DomainRuleException>(act)!.Code;

    // ---------- AUT-01: create and edit ----------

    [Test]
    public async Task Author_can_create_a_draft_and_it_is_stored()
    {
        var draft = await _service.CreateDraftAsync(_author, Content());

        var stored = await _drafts.GetAsync(draft.Id);
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.OwnerId, Is.EqualTo(_author));
        Assert.That(stored.Content.Title, Is.EqualTo("Lab one"));
    }

    [Test]
    public void Actor_without_authoring_rights_cannot_create_and_nothing_is_stored()
    {
        Assert.That(CodeOf(() => _service.CreateDraftAsync(_student, Content())), Is.EqualTo(AuthoringErrorCodes.Forbidden));
        Assert.That(_drafts.Count, Is.EqualTo(0));
    }

    [Test]
    public void Empty_actor_is_always_forbidden_even_when_the_policy_would_allow_everything()
    {
        var service = new DraftService(_drafts, new BuiltInTemplateRepository(), new AllowAllAccess());

        Assert.That(CodeOf(() => service.CreateDraftAsync(Guid.Empty, Content())), Is.EqualTo(AuthoringErrorCodes.Forbidden));
        Assert.That(_drafts.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task Owner_can_edit_and_the_change_is_persisted_with_a_new_version()
    {
        var draft = await _service.CreateDraftAsync(_author, Content());

        var edited = await _service.EditDraftAsync(_author, draft.Id, expectedVersion: 1, Content("Renamed"));

        Assert.That(edited.Version, Is.EqualTo(2));
        var stored = _drafts.Peek(draft.Id)!;
        Assert.That(stored.Content.Title, Is.EqualTo("Renamed"));
        Assert.That(stored.Version, Is.EqualTo(2));
    }

    [Test]
    public async Task Another_author_cannot_edit_the_draft_and_it_stays_unchanged()
    {
        var draft = await _service.CreateDraftAsync(_author, Content());

        Assert.That(CodeOf(() => _service.EditDraftAsync(_otherAuthor, draft.Id, 1, Content("Hijacked"))),
            Is.EqualTo(AuthoringErrorCodes.Forbidden));

        var stored = _drafts.Peek(draft.Id)!;
        Assert.That(stored.Content.Title, Is.EqualTo("Lab one"));
        Assert.That(stored.Version, Is.EqualTo(1));
    }

    [Test]
    public async Task A_student_cannot_edit_a_draft()
    {
        var draft = await _service.CreateDraftAsync(_author, Content());

        Assert.That(CodeOf(() => _service.EditDraftAsync(_student, draft.Id, 1, Content("Nope"))),
            Is.EqualTo(AuthoringErrorCodes.Forbidden));
    }

    [Test]
    public async Task Denial_is_identical_for_a_missing_draft_and_someone_elses_draft_so_ids_cannot_be_probed()
    {
        var existing = await _service.CreateDraftAsync(_author, Content());

        var foreign = CodeOf(() => _service.EditDraftAsync(_otherAuthor, existing.Id, 1, Content("x")));
        var missing = CodeOf(() => _service.EditDraftAsync(_otherAuthor, Guid.NewGuid(), 1, Content("x")));

        Assert.That(foreign, Is.EqualTo(AuthoringErrorCodes.Forbidden));
        Assert.That(missing, Is.EqualTo(foreign));
    }

    [Test]
    public void Editing_a_missing_draft_is_not_found_once_access_is_granted()
    {
        var service = new DraftService(_drafts, new BuiltInTemplateRepository(), new AllowAllAccess());

        Assert.That(CodeOf(() => service.EditDraftAsync(_author, Guid.NewGuid(), 1, Content())),
            Is.EqualTo(AuthoringErrorCodes.NotFound));
    }

    [Test]
    public async Task Stale_edit_conflicts_and_does_not_overwrite_newer_work()
    {
        var draft = await _service.CreateDraftAsync(_author, Content());
        await _service.EditDraftAsync(_author, draft.Id, 1, Content("Newer work"));

        Assert.That(CodeOf(() => _service.EditDraftAsync(_author, draft.Id, 1, Content("Stale tab"))),
            Is.EqualTo(AuthoringErrorCodes.Conflict));

        Assert.That(_drafts.Peek(draft.Id)!.Content.Title, Is.EqualTo("Newer work"));
    }

    [Test]
    public async Task Identical_edit_is_a_no_op_and_does_not_bump_the_stored_version()
    {
        var draft = await _service.CreateDraftAsync(_author, Content());

        var result = await _service.EditDraftAsync(_author, draft.Id, 1, Content());

        Assert.That(result.Version, Is.EqualTo(1));
        Assert.That(_drafts.Peek(draft.Id)!.Version, Is.EqualTo(1));
    }

    [Test]
    public async Task Draft_in_review_is_rejected_by_the_service()
    {
        var inReview = LabDraft.Rehydrate(Guid.NewGuid(), _author, null, DraftStatus.InReview, Content(), version: 2);
        await _drafts.AddAsync(inReview);

        Assert.That(CodeOf(() => _service.EditDraftAsync(_author, inReview.Id, 2, Content("Late change"))),
            Is.EqualTo(AuthoringErrorCodes.DraftNotEditable));
        Assert.That(_drafts.Peek(inReview.Id)!.Content.Title, Is.EqualTo("Lab one"));
    }

    [Test]
    public async Task Content_captured_before_an_edit_is_not_changed_by_it()
    {
        var draft = await _service.CreateDraftAsync(_author, Content("Original"));
        var captured = _drafts.Peek(draft.Id)!.Content; // what a published snapshot would hold

        await _service.EditDraftAsync(_author, draft.Id, 1, Content("Edited later"));

        Assert.That(captured.Title, Is.EqualTo("Original"));
    }

    // ---------- AUT-06: templates ----------

    [Test]
    public async Task Draft_from_template_gets_a_new_id_and_the_template_content()
    {
        var template = BuiltInTemplates.All.First(t => t.Id == BuiltInTemplates.AccessControlId);

        var draft = await _service.CreateDraftFromTemplateAsync(_author, template.Id);

        Assert.That(draft.Id, Is.Not.EqualTo(template.Id));
        Assert.That(draft.OwnerId, Is.EqualTo(_author));
        Assert.That(draft.SourceTemplateId, Is.EqualTo(template.Id));
        Assert.That(draft.Content, Is.EqualTo(template.Content));
        Assert.That(_drafts.Peek(draft.Id), Is.Not.Null);
    }

    [Test]
    public async Task Editing_a_draft_made_from_a_template_leaves_the_template_untouched()
    {
        var repo = new BuiltInTemplateRepository();
        var before = await repo.GetAsync(BuiltInTemplates.LogAnalysisId);
        var draft = await _service.CreateDraftFromTemplateAsync(_author, BuiltInTemplates.LogAnalysisId);

        await _service.EditDraftAsync(_author, draft.Id, 1, Content("Completely my own lab"));

        var after = await repo.GetAsync(BuiltInTemplates.LogAnalysisId);
        Assert.That(after!.Content, Is.EqualTo(before!.Content));
        Assert.That(after.Name, Is.EqualTo(before.Name));
    }

    [Test]
    public async Task Each_copy_of_a_template_is_a_separate_draft()
    {
        var first = await _service.CreateDraftFromTemplateAsync(_author, BuiltInTemplates.SecureConfigurationId);
        var second = await _service.CreateDraftFromTemplateAsync(_author, BuiltInTemplates.SecureConfigurationId);

        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
        Assert.That(_drafts.Count, Is.EqualTo(2));
    }

    [Test]
    public void Unknown_template_is_not_found()
    {
        Assert.That(CodeOf(() => _service.CreateDraftFromTemplateAsync(_author, Guid.NewGuid())),
            Is.EqualTo(AuthoringErrorCodes.NotFound));
        Assert.That(_drafts.Count, Is.EqualTo(0));
    }

    [Test]
    public void Actor_without_authoring_rights_cannot_copy_a_template_and_nothing_is_stored()
    {
        Assert.That(CodeOf(() => _service.CreateDraftFromTemplateAsync(_student, BuiltInTemplates.AccessControlId)),
            Is.EqualTo(AuthoringErrorCodes.Forbidden));
        Assert.That(_drafts.Count, Is.EqualTo(0));
    }
}
