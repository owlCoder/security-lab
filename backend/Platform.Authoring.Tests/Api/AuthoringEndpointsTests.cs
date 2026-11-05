using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Endpoints;
using Platform.Authoring.Application;
using Platform.Authoring.Composition;
using Platform.Authoring.Infrastructure;
using Platform.Authoring.Tests.Support;

namespace Platform.Authoring.Tests.Api;

/// <summary>
/// AUT-06 at the API boundary, with the real Access module and the real use cases behind it:
/// a copy has a new ID, the template is not modified, and nobody except an author gets in.
/// </summary>
[TestFixture]
public class AuthoringEndpointsTests
{
    private static readonly Guid TemplateId = BuiltInTemplates.AccessControlId;

    private ServiceProvider _sp = null!;

    [SetUp]
    public void SetUp() => _sp = TestHost.Build(TestHost.Author);

    [TearDown]
    public void TearDown() => _sp.Dispose();

    private Task<IResult> List(SubjectRef? subject) =>
        AuthoringEndpoints.ListTemplatesAsync(
            new DefaultHttpContext(),
            new FixedSubjectResolver(subject),
            _sp.GetRequiredService<IAccessDecision>(),
            _sp.GetRequiredService<TemplateCatalogService>(),
            CancellationToken.None);

    private Task<IResult> Copy(SubjectRef? subject, Guid templateId) =>
        AuthoringEndpoints.CopyTemplateAsync(
            templateId,
            new DefaultHttpContext(),
            new FixedSubjectResolver(subject),
            _sp.GetRequiredService<IAccessDecision>(),
            _sp.GetRequiredService<DraftService>(),
            CancellationToken.None);

    private static int Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 200;

    private static T Body<T>(IResult result) where T : class => (T)((IValueHttpResult)result).Value!;

    private int StoredDrafts => ((InMemoryDraftStore)_sp.GetRequiredService<IDraftRepository>()).Count;

    // ---------- list ----------

    [Test]
    public async Task Author_gets_the_template_list()
    {
        var result = await List(TestHost.Active(TestHost.Author));

        Assert.That(Status(result), Is.EqualTo(200));
        var body = Body<TemplateListResponse>(result);
        Assert.That(body.Templates.Count, Is.EqualTo(BuiltInTemplates.All.Count));
        Assert.That(body.CorrelationId.Length > 0, Is.True);
    }

    [Test]
    public async Task Listing_without_a_session_is_401_and_for_a_non_author_is_403()
    {
        var anonymous = await List(null);
        var student = await List(TestHost.Active(TestHost.Student));

        Assert.That(Status(anonymous), Is.EqualTo(401));
        Assert.That(Body<AuthoringErrorResponse>(anonymous).Error, Is.EqualTo("unauthenticated"));
        Assert.That(Status(student), Is.EqualTo(403));
        Assert.That(Body<AuthoringErrorResponse>(student).Error, Is.EqualTo("forbidden"));
    }

    // ---------- copy: the AUT-06 acceptance criterion ----------

    [Test]
    public async Task Copy_returns_201_with_a_new_id_owned_by_the_caller()
    {
        var result = await Copy(TestHost.Active(TestHost.Author), TemplateId);

        Assert.That(Status(result), Is.EqualTo(201));
        var draft = Body<DraftResponse>(result);
        Assert.That(draft.Id, Is.Not.EqualTo(TemplateId));
        Assert.That(draft.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(draft.SourceTemplateId, Is.EqualTo(TemplateId));
        Assert.That(draft.OwnerId, Is.EqualTo(TestHost.Author));
        Assert.That(draft.Status, Is.EqualTo("Draft"));
        Assert.That(draft.Version, Is.EqualTo(1));
        Assert.That(StoredDrafts, Is.EqualTo(1));
    }

    [Test]
    public async Task Copy_has_the_template_content_and_the_template_is_unchanged_afterwards()
    {
        var before = Body<TemplateListResponse>(await List(TestHost.Active(TestHost.Author))).Templates;
        var template = before.First(t => t.Id == TemplateId);

        var draft = Body<DraftResponse>(await Copy(TestHost.Active(TestHost.Author), TemplateId));
        var after = Body<TemplateListResponse>(await List(TestHost.Active(TestHost.Author))).Templates;

        Assert.That(draft.Title, Is.EqualTo(template.Title));
        Assert.That(draft.Goal, Is.EqualTo(template.Goal));
        Assert.That(draft.Type, Is.EqualTo(template.Type));
        Assert.That(after.Count, Is.EqualTo(before.Count));
        Assert.That(after.First(t => t.Id == TemplateId), Is.EqualTo(template));
    }

    [Test]
    public async Task Two_copies_are_two_separate_drafts()
    {
        var first = Body<DraftResponse>(await Copy(TestHost.Active(TestHost.Author), TemplateId));
        var second = Body<DraftResponse>(await Copy(TestHost.Active(TestHost.Author), TemplateId));

        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
        Assert.That(StoredDrafts, Is.EqualTo(2));
    }

    // ---------- copy: negative cases ----------

    [Test]
    public async Task Copy_without_a_session_is_401_and_stores_nothing()
    {
        var result = await Copy(null, TemplateId);

        Assert.That(Status(result), Is.EqualTo(401));
        Assert.That(StoredDrafts, Is.EqualTo(0));
    }

    [Test]
    public async Task Copy_by_a_student_is_403_and_stores_nothing()
    {
        var result = await Copy(TestHost.Active(TestHost.Student), TemplateId);

        Assert.That(Status(result), Is.EqualTo(403));
        Assert.That(Body<AuthoringErrorResponse>(result).Error, Is.EqualTo("forbidden"));
        Assert.That(StoredDrafts, Is.EqualTo(0));
    }

    [TestCase(AccountState.Blocked)]
    [TestCase(AccountState.Archived)]
    public async Task Copy_by_a_blocked_or_archived_author_is_403_and_stores_nothing(AccountState state)
    {
        var result = await Copy(new SubjectRef(TestHost.Author.ToString(), state), TemplateId);

        Assert.That(Status(result), Is.EqualTo(403));
        Assert.That(StoredDrafts, Is.EqualTo(0));
    }

    [Test]
    public async Task Copy_by_a_subject_whose_id_is_not_a_guid_is_403()
    {
        var result = await Copy(new SubjectRef("not-a-guid", AccountState.Active), TemplateId);

        Assert.That(Status(result), Is.EqualTo(403));
        Assert.That(StoredDrafts, Is.EqualTo(0));
    }

    [Test]
    public async Task Copy_of_an_unknown_template_is_404_and_stores_nothing()
    {
        var result = await Copy(TestHost.Active(TestHost.Author), Guid.NewGuid());

        Assert.That(Status(result), Is.EqualTo(404));
        Assert.That(Body<AuthoringErrorResponse>(result).Error, Is.EqualTo("template_not_found"));
        Assert.That(StoredDrafts, Is.EqualTo(0));
    }

    [Test]
    public async Task Every_error_carries_a_correlation_id_and_no_internal_details()
    {
        var results = new[]
        {
            await Copy(null, TemplateId),
            await Copy(TestHost.Active(TestHost.Student), TemplateId),
            await Copy(TestHost.Active(TestHost.Author), Guid.NewGuid()),
        };

        foreach (var result in results)
        {
            var error = Body<AuthoringErrorResponse>(result);
            Assert.That(error.CorrelationId.Length > 0, Is.True);
            Assert.That(error.Error.Contains("Exception") || error.Error.Contains("   at "), Is.False);
        }
    }

    [Test]
    public async Task Copy_goes_through_the_platform_access_decision_and_denies_when_no_policy_is_registered()
    {
        // A host that never called RegisterAuthoringAccess(): the Access module must deny by default.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TestHost.Config(TestHost.Author));
        services.AddAccessControl();
        services.AddAuthoring();
        using var unregistered = services.BuildServiceProvider();

        var result = await AuthoringEndpoints.CopyTemplateAsync(
            TemplateId,
            new DefaultHttpContext(),
            new FixedSubjectResolver(TestHost.Active(TestHost.Author)),
            unregistered.GetRequiredService<IAccessDecision>(),
            unregistered.GetRequiredService<DraftService>(),
            CancellationToken.None);

        Assert.That(Status(result), Is.EqualTo(403));
        Assert.That(((InMemoryDraftStore)unregistered.GetRequiredService<IDraftRepository>()).Count, Is.EqualTo(0));
    }
}
