
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Authorization;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.Authoring.Infrastructure;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class PrerequisiteModificationPolicyTests
{
    [Test]
    public async Task Author_CanModifyOwnLaboratory()
    {
        var authorId = Guid.NewGuid();
        var laboratory = new Laboratory("Laboratorija A", authorId);

        var repository = new InMemoryLaboratoryRepository();
        await repository.AddAsync(laboratory);

        var policy = CreatePolicy(repository, authorId);

        var request = CreateRequest(authorId, laboratory.Id);

        var allowed = await policy.EvaluateAsync(request);

        Assert.That(allowed, Is.True);
    }

    [Test]
    public async Task Author_CannotModifyAnotherAuthorsLaboratory()
    {
        var authorA = Guid.NewGuid();
        var authorB = Guid.NewGuid();

        var laboratoryB = new Laboratory("Laboratorija B", authorB);

        var repository = new InMemoryLaboratoryRepository();
        await repository.AddAsync(laboratoryB);

        var policy = CreatePolicy(repository, authorA, authorB);

        var request = CreateRequest(authorA, laboratoryB.Id);

        var allowed = await policy.EvaluateAsync(request);

        Assert.That(allowed, Is.False);
    }

    [Test]
    public async Task Student_CannotModifyLaboratory()
    {
        var studentId = Guid.NewGuid();
        var laboratory = new Laboratory("Laboratorija A", studentId);

        var repository = new InMemoryLaboratoryRepository();
        await repository.AddAsync(laboratory);

        // Student nije u listi registrovanih autora.
        var policy = CreatePolicy(repository);

        var request = CreateRequest(studentId, laboratory.Id);

        var allowed = await policy.EvaluateAsync(request);

        Assert.That(allowed, Is.False);
    }

    [Test]
    public async Task MissingLaboratoryResource_IsDenied()
    {
        var authorId = Guid.NewGuid();
        var repository = new InMemoryLaboratoryRepository();
        var policy = CreatePolicy(repository, authorId);

        var request = new AccessRequest(
            new SubjectRef(authorId.ToString(), AccountState.Active),
            "prerequisites.add",
            null,
            Guid.NewGuid().ToString());

        var allowed = await policy.EvaluateAsync(request);

        Assert.That(allowed, Is.False);
    }

    [Test]
    public async Task NonexistentLaboratory_IsDenied()
    {
        var authorId = Guid.NewGuid();
        var repository = new InMemoryLaboratoryRepository();
        var policy = CreatePolicy(repository, authorId);

        var request = CreateRequest(authorId, Guid.NewGuid());

        var allowed = await policy.EvaluateAsync(request);

        Assert.That(allowed, Is.False);
    }

    private static PrerequisiteModificationPolicy CreatePolicy(
        InMemoryLaboratoryRepository repository,
        params Guid[] authorIds)
    {
        var values = new Dictionary<string, string?>();

        for (int i = 0; i < authorIds.Length; i++)
        {
            values[$"Authoring:AuthorAccountIds:{i}"] =
                authorIds[i].ToString();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var authors = new ConfigAuthorDirectory(configuration);

        return new PrerequisiteModificationPolicy(repository, authors);
    }

    private static AccessRequest CreateRequest(
        Guid userId, Guid laboratoryId)
    {
        return new AccessRequest(
            new SubjectRef(userId.ToString(), AccountState.Active),
            "prerequisites.add",
            new ResourceRef("laboratory", laboratoryId.ToString()),
            Guid.NewGuid().ToString());
    }
}
