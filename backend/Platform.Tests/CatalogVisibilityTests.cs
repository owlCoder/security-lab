
using NUnit.Framework;
using Platform.Api.Modules.Laboratories.Application;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.Tests;

[TestFixture]
public class CatalogVisibilityTests
{
    [Test]
    public void Student_CannotViewDraftLaboratory()
    {
        var content = CreateLaboratory(
            LabVisibilityState.Draft);

        var result = CatalogVisibilityPolicy.CanView(
            content,
            CourseRole.Student);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Student_CannotViewLaboratoryOutsideTheirCourse()
    {
        var content = CreateLaboratory(
            LabVisibilityState.Published);

        // None oznacava da korisnik nije clan kursa.
        var result = CatalogVisibilityPolicy.CanView(
            content,
            CourseRole.None);

        Assert.That(result, Is.False);
    }

    private static LabPublicContent CreateLaboratory(
        LabVisibilityState state)
    {
        return LabPublicContent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            state,
            "Test laboratorija",
            "Cilj test laboratorije",
            Array.Empty<PublicTask>());
    }
}
