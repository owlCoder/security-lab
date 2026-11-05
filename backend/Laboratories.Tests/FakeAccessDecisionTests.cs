using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Infrastructure;

namespace Laboratories.Tests;

// Testovi politike pristupa: sve sto nije izricito dozvoljeno -> odbijeno.
public class FakeAccessDecisionTests
{
    private readonly FakeAccessDecision Policy = new FakeAccessDecision();

    [Test]
    public void Decide_WhenUserIsUnknown_Denies()
    {
        IIdentityContext unknownUser = new AnonymousIdentityContext();

        AccessResult result = Policy.Decide(unknownUser, LabActions.List);

        Assert.That(result.Allowed, Is.False);
        Assert.That(result.Reason, Is.EqualTo(DenyReason.Unauthenticated));
    }

    [Test]
    public void Decide_WhenActionIsUnknown_Denies()
    {
        IIdentityContext teacher = new TestUser("marko", PlatformRole.Teacher);

        AccessResult result = Policy.Decide(teacher, "laboratory.delete-everything");

        Assert.That(result.Allowed, Is.False);
        Assert.That(result.Reason, Is.EqualTo(DenyReason.UnknownAction));
    }

    [Test]
    public void Decide_WhenStudentCreatesLaboratory_Denies()
    {
        IIdentityContext student = new TestUser("ana", PlatformRole.Student);

        AccessResult result = Policy.Decide(student, LabActions.Create);

        Assert.That(result.Allowed, Is.False);
        Assert.That(result.Reason, Is.EqualTo(DenyReason.NotPermitted));
    }

    [Test]
    public void Decide_WhenTeacherCreatesLaboratory_Allows()
    {
        IIdentityContext teacher = new TestUser("marko", PlatformRole.Teacher);

        AccessResult result = Policy.Decide(teacher, LabActions.Create);

        Assert.That(result.Allowed, Is.True);
    }

    [TestCase(LabActions.Read)]
    [TestCase(LabActions.Rename)]
    public void Decide_WhenStudentReadsOrRenamesLaboratory_Denies(string action)
    {
        IIdentityContext student = new TestUser("ana", PlatformRole.Student);

        AccessResult result = Policy.Decide(student, action);

        Assert.That(result.Allowed, Is.False);
        Assert.That(result.Reason, Is.EqualTo(DenyReason.NotPermitted));
    }

    [TestCase(LabActions.Read)]
    [TestCase(LabActions.Rename)]
    public void Decide_WhenTeacherReadsOrRenamesLaboratory_Allows(string action)
    {
        IIdentityContext teacher = new TestUser("marko", PlatformRole.Teacher);

        AccessResult result = Policy.Decide(teacher, action);

        Assert.That(result.Allowed, Is.True);
    }

    //lazni korisnik za potrebe testova
    private sealed class TestUser : IIdentityContext
    {
        public TestUser(string userId, PlatformRole role)
        {
            UserId = userId;
            Role = role;
        }

        public bool IsAuthenticated => true;

        public string? UserId { get; }

        public PlatformRole? Role { get; }
    }
}