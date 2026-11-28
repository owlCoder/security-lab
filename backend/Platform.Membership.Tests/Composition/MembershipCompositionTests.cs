using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Membership.Composition;
using Platform.Membership.Contracts;
using Platform.Membership.Infrastructure.Courses;
using Platform.Membership.Tests.Testing;

namespace Platform.Membership.Tests.Composition;

// ACC-02-1: modul se uključuje jednim pozivom i bira privremene delove prema okruženju.
[TestFixture]
public sealed class MembershipCompositionTests
{
    [Test]
    public async Task Development_uses_development_courses()
    {
        await using var provider = BuildProvider("Development");
        var courses = provider.GetRequiredService<ICourseReader>();

        var active = await courses.FindAsync(DevelopmentCourses.SecurityLab);
        var archived = await courses.FindAsync(DevelopmentCourses.ArchivedCourse);

        Assert.That(courses, Is.InstanceOf<DevelopmentCourseReader>());
        Assert.That(active, Is.Not.Null);
        Assert.That(active!.IsArchived, Is.False);
        Assert.That(archived!.IsArchived, Is.True);
    }

    [Test]
    public async Task Outside_development_no_course_is_known_fail_closed()
    {
        await using var provider = BuildProvider("Production");
        var courses = provider.GetRequiredService<ICourseReader>();

        var course = await courses.FindAsync(DevelopmentCourses.SecurityLab);

        Assert.That(courses, Is.InstanceOf<UnavailableCourseReader>());
        Assert.That(course, Is.Null);
    }

    private static ServiceProvider BuildProvider(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddMembershipModule(new TestHostEnvironment(environmentName));
        return services.BuildServiceProvider();
    }
}