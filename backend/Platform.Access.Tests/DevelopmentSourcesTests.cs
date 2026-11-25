using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Development;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Policies;
using Platform.Access.Infrastructure.Resources;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests;


[TestFixture]
[Property("RequirementId", "ACC-08")]
public sealed class DevelopmentSourcesRegistrationTests
{
    [TestCase("Production")]
    [TestCase("Staging")]
    [TestCase("Testing")]
    public void ACC08_FlagOutsideDevelopment_RegistersNothing(string environmentName)
    {
        var services = new ServiceCollection();

        services.AddAccessDevelopmentSources(Env(environmentName), Config("true"));

        Assert.That(services, Is.Empty);
    }

    [TestCase("false")]
    [TestCase("0")]
    [TestCase("yes")]
    [TestCase("")]
    public void ACC08_DevelopmentWithoutExplicitTrue_RegistersNothing(string flag)
    {
        var services = new ServiceCollection();

        services.AddAccessDevelopmentSources(Env("Development"), Config(flag));

        Assert.That(services, Is.Empty);
    }

    [Test]
    public void ACC08_DevelopmentWithoutFlagKey_RegistersNothing()
    {
        var services = new ServiceCollection();

        services.AddAccessDevelopmentSources(Env("Development"), Config(null));

        Assert.That(services, Is.Empty);
    }

    [Test]
    public void ACC08_DevelopmentWithFlag_RegistersSeededSources()
    {
        var services = new ServiceCollection();

        services.AddAccessDevelopmentSources(Env("Development"), Config("true"));
        using var provider = services.BuildServiceProvider();

        var memberships = provider.GetRequiredService<IMembershipReader>();
        var descriptors = provider.GetRequiredService<IResourceDescriptorResolver>();
        var owners = provider.GetRequiredService<IResourceOwnershipResolver>();

        Assert.That(memberships, Is.InstanceOf<InMemoryMembershipReader>());
        Assert.That(descriptors, Is.InstanceOf<InMemoryResourceDescriptorResolver>());

       
        Assert.That(owners, Is.SameAs(descriptors));

        
        var attributeTypes = provider.GetServices<IResourceAttributeResolver>()
            .Select(resolver => resolver.ResourceType)
            .ToArray();
        Assert.That(attributeTypes, Is.EquivalentTo(new[] { "laboratory", "assignment", "attempt" }));
    }

    [Test]
    public void ACC08_ProductionHost_UsesEmptySourcesEvenWithFlag()
    {
        var services = new ServiceCollection();

        services.AddAccessDevelopmentSources(Env("Production"), Config("true"));
        services.AddAccessControl();
        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<IMembershipReader>(), Is.InstanceOf<EmptyMembershipReader>());
        Assert.That(
            provider.GetRequiredService<IResourceDescriptorResolver>(),
            Is.InstanceOf<EmptyResourceDescriptorResolver>());
        Assert.That(provider.GetService<IResourceOwnershipResolver>(), Is.Null);
        Assert.That(provider.GetServices<IResourceAttributeResolver>(), Is.Empty);
    }

    [Test]
    public void ACC08_AlreadyRegisteredMembershipReader_IsNotReplaced()
    {
        
        var real = new InMemoryMembershipReader();
        var services = new ServiceCollection();
        services.AddSingleton<IMembershipReader>(real);

        services.AddAccessDevelopmentSources(Env("Development"), Config("true"));
        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<IMembershipReader>(), Is.SameAs(real));
    }

    [TestCase("attribute")]
    [TestCase("descriptor")]
    [TestCase("ownership")]
    public void ACC08_DevelopmentSeed_RejectsExistingResourceSources(string source)
    {
        var services = new ServiceCollection();
        var resources = new InMemoryResourceDescriptorResolver();

        switch (source)
        {
            case "attribute":
                services.AddSingleton<IResourceAttributeResolver>(
                    new InMemoryResourceAttributeResolver("attempt"));
                break;
            case "descriptor":
                services.AddSingleton<IResourceDescriptorResolver>(resources);
                break;
            case "ownership":
                services.AddSingleton<IResourceOwnershipResolver>(resources);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown resource source.");
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddAccessDevelopmentSources(Env("Development"), Config("true")));

        Assert.That(error!.Message, Does.Contain("Disable Access:DevelopmentSources"));
    }

    [Test]
    public void ACC08_NullArguments_AreRejected()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            services.AddAccessDevelopmentSources(null!, Config("true")));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddAccessDevelopmentSources(Env("Development"), null!));
        Assert.Throws<ArgumentNullException>(() =>
            AccessDevelopmentServiceCollectionExtensions.AddAccessDevelopmentSources(
                null!, Env("Development"), Config("true")));
    }

    private static IHostEnvironment Env(string name) => new FakeEnvironment(name);

    private static IConfiguration Config(string? flag) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                flag is null
                    ? new Dictionary<string, string?>()
                    : new Dictionary<string, string?>
                    {
                        [AccessDevelopmentServiceCollectionExtensions.DevelopmentSourcesKey] = flag
                    })
            .Build();

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Platform.Access.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}


[TestFixture]
[Property("RequirementId", "ACC-08")]
public sealed class DevelopmentSeedDecisionTests
{
    private EngineFixture fixture = null!;
    private InMemoryResourceDescriptorResolver resources = null!;

    [SetUp]
    public void SetUp()
    {
        var memberships = new InMemoryMembershipReader();
        resources = new InMemoryResourceDescriptorResolver();
        AccessDevelopmentSeed.Populate(memberships, resources);

        fixture = new EngineFixture();
        foreach (var definition in CourseRolePolicies.All)
        {
            fixture.WithPolicy(
                definition.Action.Id,
                new CourseRoleAccessPolicy(definition, memberships, resources));
        }
    }

    [Test]
    public async Task ACC08_StudentReadsOwnAttempt_Allow()
    {
        var decision = await Decide(AccessDevelopmentSeed.StudentA, "attempt.read", AccessDevelopmentSeed.AttemptA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task ACC08_StudentSubmitsOwnAttempt_Allow()
    {
        var decision = await Decide(AccessDevelopmentSeed.StudentA, "attempt.submit", AccessDevelopmentSeed.AttemptA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task ACC08_StudentCreatesAttemptOnAssignmentOfOwnCourse_Allow()
    {
        var decision = await Decide(AccessDevelopmentSeed.StudentA, "attempt.create", AccessDevelopmentSeed.AssignmentA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task ACC08_StudentReadsAttemptOfAnotherCourse_Deny()
    {
        var decision = await Decide(AccessDevelopmentSeed.StudentA, "attempt.read", AccessDevelopmentSeed.AttemptB);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_StudentReadsAttemptOfClassmateInSameCourse_Deny()
    {
        
        var decision = await Decide(
            AccessDevelopmentSeed.StudentA,
            "attempt.read",
            AccessDevelopmentSeed.AttemptOfFormerStudentA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_StudentSubmitsAttemptOfClassmateInSameCourse_Deny()
    {
        var decision = await Decide(
            AccessDevelopmentSeed.StudentA,
            "attempt.submit",
            AccessDevelopmentSeed.AttemptOfFormerStudentA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_StudentOfAnotherCourseReadsAttempt_Deny()
    {
        var decision = await Decide(AccessDevelopmentSeed.StudentB, "attempt.read", AccessDevelopmentSeed.AttemptA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_StudentNotEnrolledCreatesAttempt_Deny()
    {
        var decision = await Decide(AccessDevelopmentSeed.StudentB, "attempt.create", AccessDevelopmentSeed.AssignmentA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_TeacherReadsAttemptsOfOwnCourse_Allow()
    {
        var decision = await Decide(AccessDevelopmentSeed.TeacherA, "attempt.read.course", AccessDevelopmentSeed.AttemptA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task ACC08_TeacherOfAnotherCourseReadsAttempts_Deny()
    {
        var decision = await Decide(AccessDevelopmentSeed.TeacherB, "attempt.read.course", AccessDevelopmentSeed.AttemptA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_TeacherUsesStudentOnlyAction_Deny()
    {
        var decision = await Decide(AccessDevelopmentSeed.TeacherA, "attempt.read", AccessDevelopmentSeed.AttemptA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_AssistantWithDelegatedAction_Allow()
    {
        var decision = await Decide(AccessDevelopmentSeed.AssistantA, "attempt.read.course", AccessDevelopmentSeed.AttemptA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task ACC08_AssistantWithoutDelegatedAction_Deny()
    {
        var decision = await Decide(AccessDevelopmentSeed.AssistantA, "attempt.review", AccessDevelopmentSeed.AttemptA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_StudentWhoLeftTheCourseReadsOwnAttempt_Deny()
    {
        var decision = await Decide(
            AccessDevelopmentSeed.FormerStudentA,
            "attempt.read",
            AccessDevelopmentSeed.AttemptOfFormerStudentA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_LabDraftManagement_OnlyTeacherOfThatCourse()
    {
        var teacher = await Decide(AccessDevelopmentSeed.TeacherA, "lab.draft.manage", AccessDevelopmentSeed.LabA);
        var otherTeacher = await Decide(AccessDevelopmentSeed.TeacherB, "lab.draft.manage", AccessDevelopmentSeed.LabA);
        var student = await Decide(AccessDevelopmentSeed.StudentA, "lab.draft.manage", AccessDevelopmentSeed.LabA);

        Assert.Multiple(() =>
        {
            Assert.That(teacher.Outcome, Is.EqualTo(AccessOutcome.Allow));
            Assert.That(otherTeacher.Outcome, Is.EqualTo(AccessOutcome.Deny));
            Assert.That(student.Outcome, Is.EqualTo(AccessOutcome.Deny));
        });
    }

    [Test]
    public async Task ACC08_ResourceMissingFromSeed_Deny()
    {
        var unknown = new ResourceRef("attempt", "20000000-0000-0000-0000-0000000000ff");

        var decision = await Decide(AccessDevelopmentSeed.StudentA, "attempt.read", unknown);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_AccountWithoutAnyMembership_Deny()
    {
        var decision = await Decide(Guid.NewGuid(), "lab.read", AccessDevelopmentSeed.LabA);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_BlockedAccountWithValidMembership_Deny()
    {
        var decision = await Decide(
            AccessDevelopmentSeed.StudentA,
            "attempt.read",
            AccessDevelopmentSeed.AttemptA,
            AccountState.Blocked);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
            Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.AccountNotActive));
        });
    }

    [Test]
    public async Task ACC08_EverySeededResource_HasACourse()
    {
        foreach (var resource in AccessDevelopmentSeed.AllResources)
        {
            var courseId = await resources.ResolveCourseIdAsync(resource);

            Assert.That(courseId, Is.Not.Null, $"{resource.Type}:{resource.Id} nema kurs u seed-u");
        }
    }

    private async Task<AccessDecision> Decide(
        Guid account,
        string action,
        ResourceRef resource,
        AccountState state = AccountState.Active)
    {
        var request = AccessRequestBuilder.Default()
            .WithSubject(new SubjectRef(account.ToString("D"), state))
            .WithAction(action)
            .WithResource(resource)
            .Build();

        return await fixture.Engine().DecideAsync(request);
    }
}
