using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Composition;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;
using Platform.LabVisibility.Infrastructure.Publication;

using LabVisibilityCourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.Tests.LabVisibility;

// KT2 ugovori (docs/contracts/LabVisibility-KT2.md): upis ključa i stanje objave od AUT-01/REV.
public class LabVisibilityKt2ContractTests
{
    private const string SecretAnswer = "SECRET-ANSWER-KT2";

    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private ServiceProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddLabVisibility();
        services.AddLabVisibilityAccess();
        services.AddAccessControl();
        provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        var reader = provider.GetRequiredService<InMemoryLabPublicContentReader>();
        foreach (var (content, _) in LabVisibilityDemoSeed.Create())
        {
            reader.Save(content);
        }

        provider.GetRequiredService<InMemoryCourseMembership>()
            .Set(LabVisibilityDemoSeed.DemoCourseId, StudentId, LabVisibilityCourseRole.Student);
    }

    [TearDown]
    public async Task TearDown() => await provider.DisposeAsync();

    [Test]
    public void KeyWrite_IsRegisteredAsLabAction()
    {
        Assert.That(LabVisibilityActions.All, Does.Contain(LabVisibilityActions.KeyWrite));
        Assert.That(LabVisibilityActions.KeyWrite, Is.EqualTo("lab.key.write"));
    }

    // Negativni test: upisan student nikad ne sme da upiše ključ, ni posle kartice 5.
    [Test]
    public async Task EnrolledStudent_IsDeniedKeyWrite()
    {
        var decision = await provider.GetRequiredService<IAccessDecision>().DecideAsync(
            new AccessRequest(
                new SubjectRef(StudentId.ToString(), AccountState.Active),
                LabVisibilityActions.KeyWrite,
                new ResourceRef("lab", LabVisibilityDemoSeed.DraftLabId.ToString()),
                Guid.NewGuid().ToString("N")));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task SaveEvaluatorKey_IsResolvable_AndDeniesStudent()
    {
        await using var scope = provider.CreateAsyncScope();
        var save = scope.ServiceProvider.GetRequiredService<ISaveEvaluatorKey>();

        var result = await save.ExecuteAsync(
            new SaveEvaluatorKeyCommand(
                LabVisibilityDemoSeed.DraftLabId,
                [new EvaluatorKeyAnswerInput("t1", SecretAnswer)]),
            new SubjectRef(StudentId.ToString(), AccountState.Active),
            Guid.NewGuid().ToString("N"));

        Assert.That(result.Outcome, Is.EqualTo(SaveEvaluatorKeyOutcome.NotFound));
        Assert.That(result.ErrorCode, Is.Null);
    }

    // Negativni test: komanda ne sme da ispiše odgovor ni kroz ToString (log, izuzetak, debugger).
    [Test]
    public void Command_ToString_NeverContainsAnswer()
    {
        var answer = new EvaluatorKeyAnswerInput("t1", SecretAnswer);
        var command = new SaveEvaluatorKeyCommand(LabVisibilityDemoSeed.DraftLabId, [answer]);

        Assert.That(answer.ToString(), Does.Not.Contain(SecretAnswer));
        Assert.That(command.ToString(), Does.Not.Contain(SecretAnswer));
    }

    // Pozitivan odgovor stub-a: demo laboratorije postoje kod vlasnika objave.
    [Test]
    public async Task PublicationStub_ReturnsStateForDemoLabs()
    {
        var source = provider.GetRequiredService<ILabPublicationSource>();

        var published = await source.GetAsync(LabVisibilityDemoSeed.PublishedLabId);
        var draft = await source.GetAsync(LabVisibilityDemoSeed.DraftLabId);

        Assert.That(published?.Status, Is.EqualTo(LabPublicationStatus.Published));
        Assert.That(draft?.Status, Is.EqualTo(LabPublicationStatus.Draft));
    }

    // Negativan odgovor stub-a: nepoznata laboratorija → null.
    [Test]
    public async Task PublicationStub_ReturnsNullForUnknownLab()
    {
        var source = provider.GetRequiredService<ILabPublicationSource>();

        Assert.That(await source.GetAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task PublicationStub_Set_ChangesState()
    {
        var stub = provider.GetRequiredService<StubLabPublicationSource>();
        stub.Set(new LabPublication(
            LabVisibilityDemoSeed.DraftLabId,
            Guid.NewGuid(),
            LabPublicationStatus.Published,
            DateTimeOffset.UnixEpoch));

        var draft = await provider.GetRequiredService<ILabPublicationSource>()
            .GetAsync(LabVisibilityDemoSeed.DraftLabId);

        Assert.That(draft?.Status, Is.EqualTo(LabPublicationStatus.Published));
    }
}
