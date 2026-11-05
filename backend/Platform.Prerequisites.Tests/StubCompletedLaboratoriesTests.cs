using NUnit.Framework;
using Platform.Prerequisites.Infrastructure;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class StubCompletedLaboratoriesTests
{
    [Test]
    public async Task GetCompletedAsync_ReturnsMarkedLaboratory()
    {
        var stub = new StubCompletedLaboratories();
        Guid studentId = Guid.NewGuid();
        Guid laboratoryId = Guid.NewGuid();

        stub.MarkCompleted(studentId, laboratoryId);
        IReadOnlySet<Guid> completed = await stub.GetCompletedAsync(studentId);

        Assert.That(completed, Does.Contain(laboratoryId));
    }

    [Test]
    public async Task GetCompletedAsync_OtherStudent_ReturnsEmpty()
    {
        var stub = new StubCompletedLaboratories();
        stub.MarkCompleted(Guid.NewGuid(), Guid.NewGuid());

        IReadOnlySet<Guid> completed = await stub.GetCompletedAsync(Guid.NewGuid());

        Assert.That(completed, Is.Empty);
    }
}