using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Composition;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Platform.Tests.LabVisibility;

// AUT-04, model pretnji T29: korisnik ne može da se predstavi kao evaluator i tako dobije ključ.
// Politika priznaje samo tačan ID evaluatora, a korisnički ID iz sesije je uvek GUID.
public class EvaluatorIdentitySpoofingTests
{
    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LabId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TeacherId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private ServiceProvider provider = null!;

    [SetUp]
    public async Task SetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLabVisibility();
        services.AddLabVisibilityAccess();
        services.AddAccessControl();
        provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        var content = LabPublicContent.Create(
            LabId,
            CourseId,
            TeacherId,
            LabVisibilityState.Published,
            "Analiza dnevnika",
            "Pronađi povezane događaje",
            [new PublicTask("t1", 1, "Koji događaj je prvi?", ["A", "B"])]);
        provider.GetRequiredService<InMemoryLabPublicContentReader>().Save(content);

        await provider.GetRequiredService<IEvaluatorKeyStore>().SaveAsync(
            EvaluatorKey.Create(content, [new TaskAnswerKey("t1", "SECRET-ANSWER-42")]));
    }

    [TearDown]
    public async Task TearDown() => await provider.DisposeAsync();

    private ValueTask<EvaluatorKey?> ReadAs(SubjectRef subject) =>
        provider.GetRequiredService<EvaluatorKeyReader>()
            .ReadAsync(subject, LabId, Guid.NewGuid().ToString("N"));

    // Kontrola za test: tačan ID evaluatora čita ključ, pa negativni testovi ispod imaju smisla.
    [Test]
    public async Task ExactEvaluatorId_CanReadKey()
    {
        Assert.That(
            await ReadAs(new SubjectRef(EvaluatorPrincipal.Id, AccountState.Active)),
            Is.Not.Null);
    }

    [TestCase("System:Evaluator")]
    [TestCase("SYSTEM:EVALUATOR")]
    [TestCase(" system:evaluator")]
    [TestCase("system:evaluator ")]
    [TestCase("system:evaluator\n")]
    [TestCase("system:evaluator/x")]
    [TestCase("system:")]
    [TestCase("evaluator")]
    [TestCase("")]
    public async Task EvaluatorIdVariant_IsDenied(string spoofedId)
    {
        Assert.That(await ReadAs(new SubjectRef(spoofedId, AccountState.Active)), Is.Null);
    }

    [Test]
    public async Task BlockedEvaluator_IsDenied()
    {
        Assert.That(
            await ReadAs(new SubjectRef(EvaluatorPrincipal.Id, AccountState.Blocked)),
            Is.Null);
    }

    // Korisnički ID iz sesije je GUID, pa nijedan korisnik ne može da ima ID evaluatora.
    [Test]
    public void EvaluatorId_IsNeverAValidUserId()
    {
        Assert.That(Guid.TryParse(EvaluatorPrincipal.Id, out _), Is.False);
    }
}
