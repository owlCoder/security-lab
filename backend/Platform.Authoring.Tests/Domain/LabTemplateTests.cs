using Platform.Authoring.Domain;
using Platform.Authoring.Infrastructure;

namespace Platform.Authoring.Tests.Domain;

/// <summary>AUT-06: a copy of a template has a new ID and does not modify the source template.</summary>
[TestFixture]
public class LabTemplateTests
{
    private static LabTemplate NewTemplate() =>
        LabTemplate.Create(Guid.NewGuid(), "Starter", LabContent.Create("Starter title", "Starter goal", LabType.LogAnalysis));

    private static string CodeOf(TestDelegate act) => Assert.Throws<DomainRuleException>(act)!.Code;

    [Test]
    public void Draft_from_template_has_a_new_id_and_copies_the_content()
    {
        var template = NewTemplate();
        var draftId = Guid.NewGuid();
        var owner = Guid.NewGuid();

        var draft = template.CreateDraft(draftId, owner);

        Assert.That(draft.Id, Is.EqualTo(draftId));
        Assert.That(draft.Id, Is.Not.EqualTo(template.Id));
        Assert.That(draft.OwnerId, Is.EqualTo(owner));
        Assert.That(draft.SourceTemplateId, Is.EqualTo(template.Id));
        Assert.That(draft.Content, Is.EqualTo(template.Content));
        Assert.That(draft.Status, Is.EqualTo(DraftStatus.Draft));
        Assert.That(draft.Version, Is.EqualTo(1));
    }

    [Test]
    public void Editing_the_copy_does_not_modify_the_source_template()
    {
        var template = NewTemplate();
        var contentBefore = template.Content;
        var draft = template.CreateDraft(Guid.NewGuid(), Guid.NewGuid());

        draft.Edit(LabContent.Create("Totally different", "Changed", LabType.ThreatModel), expectedVersion: 1);

        Assert.That(template.Content, Is.EqualTo(contentBefore));
        Assert.That(template.Content.Title, Is.EqualTo("Starter title"));
        Assert.That(template.Content.Type, Is.EqualTo(LabType.LogAnalysis));
        Assert.That(template.Name, Is.EqualTo("Starter"));
    }

    [Test]
    public void Two_copies_of_one_template_are_independent()
    {
        var template = NewTemplate();
        var first = template.CreateDraft(Guid.NewGuid(), Guid.NewGuid());
        var second = template.CreateDraft(Guid.NewGuid(), Guid.NewGuid());

        first.Edit(LabContent.Create("Only the first changes", "", LabType.LogAnalysis), expectedVersion: 1);

        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
        Assert.That(second.Content.Title, Is.EqualTo("Starter title"));
        Assert.That(second.Version, Is.EqualTo(1));
    }

    [Test]
    public void An_identifier_is_never_reused_the_template_id_cannot_become_a_draft_id()
    {
        var template = NewTemplate();

        Assert.That(CodeOf(() => template.CreateDraft(template.Id, Guid.NewGuid())), Is.EqualTo(AuthoringErrorCodes.InvalidId));
    }

    [Test]
    public void Draft_from_template_still_requires_an_owner()
    {
        var template = NewTemplate();

        Assert.That(CodeOf(() => template.CreateDraft(Guid.NewGuid(), Guid.Empty)), Is.EqualTo(AuthoringErrorCodes.InvalidId));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("bad\nname")]
    public void Template_needs_a_clean_name(string? name)
    {
        var content = LabContent.Create("T", "", LabType.AccessControl);

        Assert.That(CodeOf(() => LabTemplate.Create(Guid.NewGuid(), name, content)), Is.EqualTo(AuthoringErrorCodes.InvalidName));
    }

    [Test]
    public void Template_needs_an_id()
    {
        var content = LabContent.Create("T", "", LabType.AccessControl);

        Assert.That(CodeOf(() => LabTemplate.Create(Guid.Empty, "Name", content)), Is.EqualTo(AuthoringErrorCodes.InvalidId));
    }

    [Test]
    public void Built_in_templates_have_unique_ids_and_valid_content()
    {
        var all = BuiltInTemplates.All;

        Assert.That(all.Count, Is.EqualTo(3));
        Assert.That(all.Select(t => t.Id).Distinct().Count(), Is.EqualTo(all.Count));
        Assert.That(all.Select(t => t.Content.Type).Distinct().Count(), Is.EqualTo(all.Count));
        Assert.That(all.All(t => t.Id != Guid.Empty), Is.True);
    }
}
