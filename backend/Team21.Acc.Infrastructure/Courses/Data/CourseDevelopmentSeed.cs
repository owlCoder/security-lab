using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Courses;

namespace Team21.Acc.Infrastructure.Courses.Data;

/// <summary>Sample data. Only the Development demo seed calls it, migrations never do.</summary>
public static class CourseDevelopmentSeed
{
    private static readonly Guid SampleActor = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static async Task SeedAsync(
        ICoursesDbContext context, string environmentName, CancellationToken cancellationToken = default)
    {
        if (environmentName is not ("Development" or "Testing"))
            throw new InvalidOperationException("Sample courses are allowed only in Development or Testing.");

        var samples = new[]
        {
            Create("21000000-0000-0000-0000-000000000001", "OIB-2026", "Osnove informacione bezbednosti",
                "Laboratorijske vežbe: kontrola pristupa i analiza bezbednosnih događaja."),
            Create("21000000-0000-0000-0000-000000000002", "WEB-2026", "Bezbednost veb aplikacija",
                "Razvojni kurs za proveru autorizacije i validacije ulaza."),
            Create("21000000-0000-0000-0000-000000000003", "OIB-2025", "OIB — prethodna generacija",
                "Arhivirani testni kurs; postojeća istorija ostaje dostupna.", archived: true)
        };

        var ids = samples.Select(course => course.Id).ToArray();
        // Include soft-deleted samples: seeding must never recreate or overwrite existing records.
        var existing = await context.Courses.IgnoreQueryFilters()
            .Where(course => ids.Contains(course.Id))
            .Select(course => course.Id).ToListAsync(cancellationToken);

        context.Courses.AddRange(samples.Where(course => !existing.Contains(course.Id)));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static Course Create(string id, string code, string name, string description, bool archived = false)
    {
        var course = new Course(code, name, description, SampleActor) { Id = Guid.Parse(id) };
        if (archived)
            course.Archive(SampleActor);
        return course;
    }
}
