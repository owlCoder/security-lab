
using NUnit.Framework;
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;

namespace Platform.Tests;

[TestFixture]
public class CatalogFilterTests
{
    [Test]
    public void Apply_FiltersLaboratoriesBySearchDifficultyTypeAndTag()
    {
        var laboratories = new[]
        {
            new CatalogLaboratoryResponse(
                Guid.NewGuid(),
                "SQL Injection",
                "Published",
                "Learn SQL injection",
                "Beginner",
                "Web",
                new[] { "security", "sql" }),

            new CatalogLaboratoryResponse(
                Guid.NewGuid(),
                "Network Security",
                "Published",
                "Learn network protection",
                "Advanced",
                "Network",
                new[] { "network" })
        };

        var filter = new CatalogLaboratoryFilter(
            "sql", "beginner", "web", "SECURITY");

        var result = CatalogFilterService.Apply(laboratories, filter);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("SQL Injection"));
    }
}
