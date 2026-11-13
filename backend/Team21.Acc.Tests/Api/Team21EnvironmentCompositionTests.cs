using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Team21.Acc.Api;
using Team21.Acc.Core.Courses;
using Team21.Acc.Core.Groups;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Infrastructure.Courses.Services;
using Team21.Acc.Infrastructure.Groups.Defaults;
using Team21.Acc.Infrastructure.Groups.Fakes;
using Team21.Acc.Infrastructure.Groups.Services;

namespace Team21.Acc.Tests.Api;

public class Team21EnvironmentCompositionTests
{
    [TestCase("Development", true)]
    [TestCase("Testing", false)]
    [TestCase("Production", false)]
    public async Task ModuleDefaultsRemainExplicitAndNonDevelopmentDeniesKnownDemoUser(
        string environment, bool development)
    {
        // Inspect module composition without starting Program, hosted seeders or a database.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment, Args = []
        });
        builder.AddTeam21Acc();
        var services = builder.Services;

        AssertRegistration(typeof(IAccountStatusReader), development
            ? typeof(FakeAccountStatusReader) : typeof(DenyAllAccountStatusReader));
        AssertRegistration(typeof(ICourseMembershipReader), development
            ? typeof(FakeCourseMembershipReader) : typeof(NoCourseMembershipReader));
        AssertRegistration(typeof(ICourseStateReader), typeof(CourseStateReader));
        AssertRegistration(typeof(IGroupMembershipReader), typeof(GroupMembershipReader));
        AssertRegistration(typeof(IMaterialAccessRuleReader), typeof(MaterialAccessRuleReader));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountStatusReader>();
        Assert.That(await accounts.GetStatusAsync(DemoIds.Teacher),
            Is.EqualTo(development ? AccountStatus.Active : AccountStatus.Inactive));
        var permissions = scope.ServiceProvider.GetRequiredService<ICourseAuthorization>();
        Assert.That(await permissions.CanViewAsync(DemoIds.Teacher, DemoIds.Course), Is.EqualTo(development));

        void AssertRegistration(Type contract, Type implementation)
        {
            var registrations = services.Where(d => d.ServiceType == contract).ToArray();
            Assert.That(registrations, Has.Length.EqualTo(1), contract.Name);
            Assert.That(registrations[0].Lifetime, Is.EqualTo(ServiceLifetime.Scoped), contract.Name);
            Assert.That(registrations[0].ImplementationType, Is.EqualTo(implementation), contract.Name);
        }
    }
}
