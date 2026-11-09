using Platform.Api.Modules.Laboratories.Application;
using Platform.LabVisibility.Contracts;

namespace Laboratories.Tests;

// LAB-03 otvaranje: svaki ishod politike ima svoj stabilan kod i svoj test.
[TestFixture]
public sealed class LabOpenPolicyTests
{
    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Test]
    public void DecideOpen_WhenStudentOnCourse_Allows()
    {
        LabOpenDecision decision = LabOpenPolicy.DecideOpen(CourseId, CourseRole.Student, isCreator: false);

        Assert.That(decision, Is.EqualTo(new LabOpenDecision(true, LabOpenCodes.CourseStudent)));
    }

    [Test]
    public void DecideOpen_WhenTeacherOnCourseIsCreator_Allows()
    {
        LabOpenDecision decision = LabOpenPolicy.DecideOpen(CourseId, CourseRole.Teacher, isCreator: true);

        Assert.That(decision, Is.EqualTo(new LabOpenDecision(true, LabOpenCodes.CourseTeacher)));
    }

    [Test]
    public void DecideOpen_WhenTeacherOnCourseIsNotCreator_Denies()
    {
        LabOpenDecision decision = LabOpenPolicy.DecideOpen(CourseId, CourseRole.Teacher, isCreator: false);

        Assert.That(decision, Is.EqualTo(new LabOpenDecision(false, LabOpenCodes.NotOwner)));
    }

    [Test]
    public void DecideOpen_WhenNotMemberOfCourse_Denies()
    {
        // I autor koji vise nije na kursu gubi pristup.
        LabOpenDecision decision = LabOpenPolicy.DecideOpen(CourseId, CourseRole.None, isCreator: true);

        Assert.That(decision, Is.EqualTo(new LabOpenDecision(false, LabOpenCodes.NotCourseMember)));
    }

    [TestCase(CourseRole.Student)]
    [TestCase(CourseRole.Teacher)]
    public void DecideOpen_WhenLaboratoryHasNoCourse_DeniesEveryone(CourseRole role)
    {
        LabOpenDecision decision = LabOpenPolicy.DecideOpen(Guid.Empty, role, isCreator: true);

        Assert.That(decision, Is.EqualTo(new LabOpenDecision(false, LabOpenCodes.NoCourse)));
    }

    [Test]
    public void DecideOpen_WithUnknownRole_DeniesByDefault()
    {
        LabOpenDecision decision = LabOpenPolicy.DecideOpen(CourseId, (CourseRole)99, isCreator: true);

        Assert.That(decision.Allowed, Is.False);
    }
}
