using Platform.LabLocalization.Domain;
using Platform.LabLocalization.Infrastructure;

namespace Platform.LabLocalization.Tests.Infrastructure;

using LabLocalization = Platform.LabLocalization.Domain.LabLocalization;

[TestFixture]
public class InMemoryLabLocalizationRepositoryTests
{
    private static LabLocalization Translation(Guid revisionId, LabLanguage language, string title = "Title") =>
        LabLocalization.Create(revisionId, language, title, "", "");

    [Test]
    public async Task Stored_translation_is_found_by_revision_and_language()
    {
        var repository = new InMemoryLabLocalizationRepository();
        var revisionId = Guid.NewGuid();
        var translation = Translation(revisionId, LabLanguage.En);

        await repository.AddAsync(translation);

        Assert.That(await repository.GetAsync(revisionId, LabLanguage.En), Is.EqualTo(translation));
        Assert.That(await repository.GetAsync(revisionId, LabLanguage.Sr), Is.Null);
        Assert.That(await repository.GetAsync(Guid.NewGuid(), LabLanguage.En), Is.Null);
    }

    [Test]
    public async Task Second_translation_for_the_same_revision_and_language_is_rejected_and_the_first_is_kept()
    {
        var repository = new InMemoryLabLocalizationRepository();
        var revisionId = Guid.NewGuid();
        await repository.AddAsync(Translation(revisionId, LabLanguage.En, "First"));

        var error = Assert.ThrowsAsync<DomainRuleException>(() => repository.AddAsync(Translation(revisionId, LabLanguage.En, "Second")));

        Assert.That(error!.Code, Is.EqualTo(LocalizationErrorCodes.Conflict));
        Assert.That((await repository.GetAsync(revisionId, LabLanguage.En))!.Title, Is.EqualTo("First"));
    }
}
