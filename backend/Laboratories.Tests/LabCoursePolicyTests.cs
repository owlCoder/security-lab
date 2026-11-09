using Platform.Api.Modules.Laboratories.Application;
using Platform.LabVisibility.Contracts;

namespace Laboratories.Tests;

// LAB-03: svaki ishod politike ima svoj stabilan kod i svoj test.
[TestFixture]
public sealed class LabCoursePolicyTests
{
    [Test]
    public void DecideCreate_WhenTeacherOnCourse_Allows()
    {
        LabCourseDecision decision = LabCoursePolicy.DecideCreate(CourseRole.Teacher);

        Assert.That(decision, Is.EqualTo(new LabCourseDecision(true, LabCourseCodes.CourseTeacher)));
    }

    [TestCase(CourseRole.Student)]
    [TestCase(CourseRole.None)]
    public void DecideCreate_WhenNotTeacherOnCourse_Denies(CourseRole role)
    {
        LabCourseDecision decision = LabCoursePolicy.DecideCreate(role);

        Assert.That(decision, Is.EqualTo(new LabCourseDecision(false, LabCourseCodes.NotCourseTeacher)));
    }

    [Test]
    public void DecideCreate_WithUnknownRole_DeniesByDefault()
    {
        LabCourseDecision decision = LabCoursePolicy.DecideCreate((CourseRole)99);

        Assert.That(decision.Allowed, Is.False);
    }
}
