using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Composition;
using Platform.LabVisibility.Contracts;

using LabVisibilityCourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.Tests.LabVisibility;

public class LabVisibilitySkeletonTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        // #185: radnje se prijavljuju pre AddAccessControl(), kao u Program.cs.
        services.AddLabVisibility();
        services.AddLabVisibilityAccess();
        services.AddAccessControl();

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
    }

    private static AccessRequest ActiveStudentRequest(string action) =>
        new(
            new SubjectRef(
                "22222222-2222-2222-2222-222222222222",
                AccountState.Active),
            action,
            new ResourceRef("lab", Guid.NewGuid().ToString()),
            Guid.NewGuid().ToString("N"));

    // Nepoznata laboratorija mora biti odbijena za svaku akciju.
    [TestCaseSource(typeof(LabVisibilityActions), nameof(LabVisibilityActions.All))]
    public async Task UnknownLab_IsDeniedForEveryAction(string action)
    {
        await using var provider = BuildProvider();

        var decision = await provider
            .GetRequiredService<IAccessDecision>()
            .DecideAsync(ActiveStudentRequest(action));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task UnregisteredLabAction_IsDeniedAsUnknown()
    {
        await using var provider = BuildProvider();

        var decision = await provider
            .GetRequiredService<IAccessDecision>()
            .DecideAsync(ActiveStudentRequest("lab.answers.dump"));

        Assert.That(
            decision.DenialReason,
            Is.EqualTo(AccessDenialReason.UnknownAction));
    }

    [Test]
    public void RegisteringTwice_FailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddLabVisibility();
        services.AddLabVisibilityAccess();

        Assert.Throws<InvalidOperationException>(
            () => services.AddLabVisibilityAccess());
    }

    [Test]
    public async Task UnknownMember_HasNoRoleOnCourse()
    {
        await using var provider = BuildProvider();

        var role = await provider
            .GetRequiredService<ICourseMembership>()
            .GetRoleAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.That(role, Is.EqualTo(LabVisibilityCourseRole.None));
    }
}