using Platform.LabLocalization.Application;
using Platform.LabLocalization.Domain;
using Platform.LabLocalization.Infrastructure;
using Platform.LabLocalization.Tests.Fakes;

namespace Platform.LabLocalization.Tests.Application;

[TestFixture]
public class AddLabLocalizationTests
{
    private static readonly Guid TeacherId = Guid.NewGuid();
    private static readonly Guid StudentId = Guid.NewGuid();
    private static readonly Guid LabId = Guid.NewGuid();
    private static readonly Guid RevisionId = Guid.NewGuid();
    private const string EvaluatorVersion = "evaluator-v1";

    private FakeRevisionReader _revisions = null!;
    private InMemoryLabLocalizationRepository _localizations = null!;
    private AddLabLocalization _add = null!;

    [SetUp]
    public void SetUp()
    {
        _revisions = new FakeRevisionReader();
        _revisions.SetCurrent(LabId, new RevisionInfo(RevisionId, EvaluatorVersion));
        _localizations = new InMemoryLabLocalizationRepository();
        _add = new AddLabLocalization(new FakeLocalizationAccess(TeacherId), _revisions, _localizations);
    }

    private Task Add(Guid actorId, string? language = "en", string? title = "Access control", Guid? labId = null, Guid? revisionId = null) =>
        _add.ExecuteAsync(actorId, labId ?? LabId, revisionId ?? RevisionId, language, title, "Description", "Instructions");

    private static string CodeOf(AsyncTestDelegate act) => Assert.ThrowsAsync<DomainRuleException>(act)!.Code;

    private async Task AssertNothingStored()
    {
        Assert.That(await _localizations.GetAsync(RevisionId, LabLanguage.En), Is.Null);
        Assert.That(await _localizations.GetAsync(RevisionId, LabLanguage.Sr), Is.Null);
    }

    [Test]
    public async Task Teacher_adds_a_translation_and_it_can_be_read_on_the_same_revision_and_evaluator()
    {
        await Add(TeacherId, "en", "Access control");

        var content = await new GetLabContent(_revisions, _localizations).ExecuteAsync(LabId, "en");

        Assert.That(content.Title, Is.EqualTo("Access control"));
        Assert.That(content.Language, Is.EqualTo(LabLanguage.En));
        Assert.That(content.RevisionId, Is.EqualTo(RevisionId));
        Assert.That(content.EvaluatorVersion, Is.EqualTo(EvaluatorVersion));
    }

    [Test]
    public async Task Student_cannot_add_a_translation()
    {
        Assert.That(CodeOf(() => Add(StudentId)), Is.EqualTo(LocalizationErrorCodes.Forbidden));

        await AssertNothingStored();
    }

    [Test]
    public async Task Unknown_actor_cannot_add_a_translation()
    {
        Assert.That(CodeOf(() => Add(Guid.Empty)), Is.EqualTo(LocalizationErrorCodes.Forbidden));

        await AssertNothingStored();
    }

    [Test]
    public void Student_gets_the_same_answer_for_a_revision_that_does_not_exist()
    {
        Assert.That(CodeOf(() => Add(StudentId, revisionId: Guid.NewGuid())), Is.EqualTo(LocalizationErrorCodes.Forbidden));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("xx")]
    [TestCase("1")]
    public async Task Language_must_be_stated_and_supported(string? language)
    {
        Assert.That(CodeOf(() => Add(TeacherId, language)), Is.EqualTo(LocalizationErrorCodes.InvalidLanguage));

        await AssertNothingStored();
    }

    [Test]
    public void Revision_that_does_not_exist_is_not_found()
    {
        Assert.That(CodeOf(() => Add(TeacherId, revisionId: Guid.NewGuid())), Is.EqualTo(LocalizationErrorCodes.NotFound));
    }

    [Test]
    public async Task Revision_of_another_laboratory_is_not_found()
    {
        Assert.That(CodeOf(() => Add(TeacherId, labId: Guid.NewGuid())), Is.EqualTo(LocalizationErrorCodes.NotFound));

        await AssertNothingStored();
    }

    [Test]
    public async Task Invalid_text_is_rejected_and_nothing_is_stored()
    {
        Assert.That(CodeOf(() => Add(TeacherId, title: "")), Is.EqualTo(LocalizationErrorCodes.InvalidTitle));

        await AssertNothingStored();
    }

    [Test]
    public async Task Second_translation_in_the_same_language_does_not_overwrite_the_first()
    {
        await Add(TeacherId, "en", "First");

        Assert.That(CodeOf(() => Add(TeacherId, "en", "Second")), Is.EqualTo(LocalizationErrorCodes.Conflict));
        Assert.That((await _localizations.GetAsync(RevisionId, LabLanguage.En))!.Title, Is.EqualTo("First"));
    }
}
