using Platform.LabLocalization.Application;
using Platform.LabLocalization.Domain;
using Platform.LabLocalization.Infrastructure;
using Platform.LabLocalization.Tests.Fakes;

namespace Platform.LabLocalization.Tests.Application;

using LabLocalization = Platform.LabLocalization.Domain.LabLocalization;

[TestFixture]
public class GetLabContentTests
{
    private static readonly Guid LabId = Guid.NewGuid();
    private static readonly Guid RevisionId = Guid.NewGuid();
    private const string EvaluatorVersion = "evaluator-v1";
    private static readonly RevisionText SerbianText = new("Kontrola pristupa", "Opis", "Uputstvo");

    private FakeRevisionReader _revisions = null!;
    private InMemoryLabLocalizationRepository _localizations = null!;
    private GetLabContent _getContent = null!;

    [SetUp]
    public void SetUp()
    {
        _revisions = new FakeRevisionReader();
        _revisions.SetCurrent(LabId, new RevisionInfo(RevisionId, EvaluatorVersion, SerbianText, IsDraft: false));
        _localizations = new InMemoryLabLocalizationRepository();
        _getContent = new GetLabContent(_revisions, _localizations);
    }

    private Task AddTranslation(LabLanguage language, string title, Guid? revisionId = null) =>
        _localizations.AddAsync(LabLocalization.Create(revisionId ?? RevisionId, language, title, title + " description", title + " instructions"));

    private static string CodeOf(AsyncTestDelegate act) => Assert.ThrowsAsync<DomainRuleException>(act)!.Code;

    [Test]
    public async Task Sr_and_en_show_the_same_revision_and_evaluator_with_different_text()
    {
        await AddTranslation(LabLanguage.En, "Access control");

        var sr = await _getContent.ExecuteAsync(LabId, "sr");
        var en = await _getContent.ExecuteAsync(LabId, "en");

        Assert.That(en.RevisionId, Is.EqualTo(sr.RevisionId));
        Assert.That(en.EvaluatorVersion, Is.EqualTo(sr.EvaluatorVersion));
        Assert.That(sr.Title, Is.EqualTo("Kontrola pristupa"));
        Assert.That(en.Title, Is.EqualTo("Access control"));
        Assert.That(en.Description, Is.Not.EqualTo(sr.Description));
        Assert.That(en.Instructions, Is.Not.EqualTo(sr.Instructions));
    }

    [Test]
    public async Task Serbian_text_is_the_revision_text_without_any_stored_translation()
    {
        var content = await _getContent.ExecuteAsync(LabId, "sr");

        Assert.That(content.Language, Is.EqualTo(LabLanguage.Sr));
        Assert.That((content.Title, content.Description, content.Instructions),
            Is.EqualTo((SerbianText.Title, SerbianText.Description, SerbianText.Instructions)));
    }

    [Test]
    public async Task Stored_sr_record_never_replaces_the_revision_text()
    {
        await AddTranslation(LabLanguage.Sr, "Neodobren tekst");

        var content = await _getContent.ExecuteAsync(LabId, "sr");

        Assert.That(content.Title, Is.EqualTo(SerbianText.Title));
    }

    [TestCase(null)]
    [TestCase("")]
    public async Task No_requested_language_returns_sr(string? requested)
    {
        await AddTranslation(LabLanguage.En, "Access control");

        var content = await _getContent.ExecuteAsync(LabId, requested);

        Assert.That(content.Language, Is.EqualTo(LabLanguage.Sr));
        Assert.That(content.Title, Is.EqualTo("Kontrola pristupa"));
    }

    [Test]
    public async Task Missing_translation_falls_back_to_sr_on_the_same_revision()
    {
        var content = await _getContent.ExecuteAsync(LabId, "en");

        Assert.That(content.Language, Is.EqualTo(LabLanguage.Sr));
        Assert.That(content.Title, Is.EqualTo("Kontrola pristupa"));
        Assert.That(content.RevisionId, Is.EqualTo(RevisionId));
        Assert.That(content.EvaluatorVersion, Is.EqualTo(EvaluatorVersion));
    }

    [TestCase("xx")]
    [TestCase("1")]
    public void Unsupported_language_is_rejected(string requested)
    {
        Assert.That(CodeOf(() => _getContent.ExecuteAsync(LabId, requested)), Is.EqualTo(LocalizationErrorCodes.InvalidLanguage));
    }

    [Test]
    public void Laboratory_without_a_current_revision_is_not_found()
    {
        Assert.That(CodeOf(() => _getContent.ExecuteAsync(Guid.NewGuid(), "sr")), Is.EqualTo(LocalizationErrorCodes.NotFound));
    }

    [Test]
    public async Task Translation_of_another_revision_is_never_shown()
    {
        await AddTranslation(LabLanguage.En, "Old revision text", revisionId: Guid.NewGuid());

        var content = await _getContent.ExecuteAsync(LabId, "en");

        Assert.That(content.Title, Is.EqualTo("Kontrola pristupa"));
    }
}
