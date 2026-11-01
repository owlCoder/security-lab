using Platform.Authoring.Domain;

namespace Platform.Authoring.Tests.Domain;

/// <summary>AUT-01: a draft can be created and changed without affecting existing attempts.</summary>
[TestFixture]
public class LabDraftTests
{
    private static LabContent Content(string title = "Lab one", string goal = "Goal") =>
        LabContent.Create(title, goal, LabType.AccessControl);

    private static LabDraft NewDraft() => LabDraft.Create(Guid.NewGuid(), Guid.NewGuid(), Content());

    private static string CodeOf(TestDelegate act) => Assert.Throws<DomainRuleException>(act)!.Code;

    [Test]
    public void New_draft_is_in_draft_state_with_version_one()
    {
        var owner = Guid.NewGuid();
        var draft = LabDraft.Create(Guid.NewGuid(), owner, Content());

        Assert.That(draft.Status, Is.EqualTo(DraftStatus.Draft));
        Assert.That(draft.Version, Is.EqualTo(1));
        Assert.That(draft.OwnerId, Is.EqualTo(owner));
        Assert.That(draft.SourceTemplateId, Is.Null);
    }

    [Test]
    public void Empty_draft_or_owner_id_is_rejected()
    {
        Assert.That(CodeOf(() => LabDraft.Create(Guid.Empty, Guid.NewGuid(), Content())), Is.EqualTo(AuthoringErrorCodes.InvalidId));
        Assert.That(CodeOf(() => LabDraft.Create(Guid.NewGuid(), Guid.Empty, Content())), Is.EqualTo(AuthoringErrorCodes.InvalidId));
    }

    [Test]
    public void Edit_replaces_content_and_bumps_version()
    {
        var draft = NewDraft();

        var changed = draft.Edit(Content("Renamed"), expectedVersion: 1);

        Assert.That(changed, Is.True);
        Assert.That(draft.Content.Title, Is.EqualTo("Renamed"));
        Assert.That(draft.Version, Is.EqualTo(2));
    }

    [Test]
    public void Edit_with_identical_content_changes_nothing()
    {
        var draft = NewDraft();

        var changed = draft.Edit(Content(), expectedVersion: 1);

        Assert.That(changed, Is.False);
        Assert.That(draft.Version, Is.EqualTo(1));
    }

    [Test]
    public void Edit_with_a_stale_version_conflicts_and_keeps_current_content()
    {
        var draft = NewDraft();
        draft.Edit(Content("First"), expectedVersion: 1);

        var code = CodeOf(() => draft.Edit(Content("Stale writer"), expectedVersion: 1));

        Assert.That(code, Is.EqualTo(AuthoringErrorCodes.Conflict));
        Assert.That(draft.Content.Title, Is.EqualTo("First"));
        Assert.That(draft.Version, Is.EqualTo(2));
    }

    [Test]
    public void Edit_with_a_version_from_the_future_conflicts_too()
    {
        var draft = NewDraft();

        Assert.That(CodeOf(() => draft.Edit(Content("Other"), expectedVersion: 5)), Is.EqualTo(AuthoringErrorCodes.Conflict));
    }

    [Test]
    public void Draft_in_review_cannot_be_edited()
    {
        var draft = LabDraft.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), null, DraftStatus.InReview, Content(), version: 3);

        Assert.That(CodeOf(() => draft.Edit(Content("Sneaky"), expectedVersion: 3)), Is.EqualTo(AuthoringErrorCodes.DraftNotEditable));
        Assert.That(draft.Content.Title, Is.EqualTo("Lab one"));
        Assert.That(draft.Version, Is.EqualTo(3));
    }

    [Test]
    public void Editing_a_draft_does_not_alter_content_captured_earlier()
    {
        // A revision snapshot (REV-01) captures the content object. Because LabContent is immutable,
        // later draft edits can only replace the draft's reference, never change the captured value.
        var draft = NewDraft();
        var captured = draft.Content;

        draft.Edit(Content("Edited afterwards", "New goal"), expectedVersion: 1);

        Assert.That(captured.Title, Is.EqualTo("Lab one"));
        Assert.That(captured.Goal, Is.EqualTo("Goal"));
        Assert.That(draft.Content.Title, Is.EqualTo("Edited afterwards"));
    }

    [Test]
    public void Rehydrate_rejects_impossible_stored_state()
    {
        Assert.That(CodeOf(() => LabDraft.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), null, DraftStatus.Draft, Content(), version: 0)),
            Is.EqualTo(AuthoringErrorCodes.InvalidVersion));
        Assert.That(CodeOf(() => LabDraft.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), null, (DraftStatus)42, Content(), version: 1)),
            Is.EqualTo(AuthoringErrorCodes.InvalidVersion));
    }
}
