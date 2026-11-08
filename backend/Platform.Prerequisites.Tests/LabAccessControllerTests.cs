using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Access.Domain.Models;
using Platform.Api.Controllers;
using Platform.Api.Interfaces.Identity;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Domain;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class LabAccessControllerTests
{
    private static readonly Guid LabId = Guid.NewGuid();
    private static readonly Guid CourseId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid MissingLabId = Guid.NewGuid();

    [Test]
    public async Task Get_UnauthenticatedUser_Returns401()
    {
        var controller = CreateController(null, Lab(LabVisibilityState.Published), CourseRole.Student, Unlocked());

        var result = await controller.Get(LabId, CancellationToken.None);

        Assert.That(result, Is.TypeOf<UnauthorizedResult>());
    }

    [Test]
    public async Task Get_UnknownLaboratory_Returns404()
    {
        var controller = CreateController(ActiveUser(), null, CourseRole.Student, Unlocked());

        var result = await controller.Get(LabId, CancellationToken.None);

        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task Get_StudentAndDraftLaboratory_Returns404()
    {
        var access = Unlocked();
        var controller = CreateController(ActiveUser(), Lab(LabVisibilityState.Draft), CourseRole.Student, access);

        var result = await controller.Get(LabId, CancellationToken.None);

        Assert.That(result, Is.TypeOf<NotFoundResult>());
        Assert.That(access.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task Get_UserFromOtherCourse_Returns404()
    {
        var controller = CreateController(ActiveUser(), Lab(LabVisibilityState.Published), CourseRole.None, Unlocked());

        var result = await controller.Get(LabId, CancellationToken.None);

        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task Get_Teacher_ReturnsUnlockedWithoutCheckingPrerequisites()
    {
        var access = LockedBy(MissingLabId);
        var controller = CreateController(ActiveUser(), Lab(LabVisibilityState.Draft), CourseRole.Teacher, access);

        var body = OkBody(await controller.Get(LabId, CancellationToken.None));

        Assert.That(body.IsUnlocked, Is.True);
        Assert.That(body.MissingPrerequisites, Is.Empty);
        Assert.That(access.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task Get_StudentWithCompletedPrerequisites_ReturnsUnlocked()
    {
        var controller = CreateController(ActiveUser(), Lab(LabVisibilityState.Published), CourseRole.Student, Unlocked());

        var body = OkBody(await controller.Get(LabId, CancellationToken.None));

        Assert.That(body.IsUnlocked, Is.True);
        Assert.That(body.MissingPrerequisites, Is.Empty);
    }

    [Test]
    public async Task Get_StudentWithMissingPrerequisite_ReturnsLockedWithMissingId()
    {
        var controller = CreateController(ActiveUser(), Lab(LabVisibilityState.Published), CourseRole.Student, LockedBy(MissingLabId));

        var body = OkBody(await controller.Get(LabId, CancellationToken.None));

        Assert.That(body.IsUnlocked, Is.False);
        Assert.That(body.MissingPrerequisites, Is.EqualTo(new[] { MissingLabId }));
    }

    private static LabAccessController CreateController(
        SubjectRef? subject,
        LabPublicContent? content,
        CourseRole role,
        FakeAccessService access)
    {
        var controller = new LabAccessController(
            access,
            new FixedContentReader(content),
            new FixedMembership(role),
            new FixedSubjectResolver(subject));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static SubjectRef ActiveUser() =>
        new(UserId.ToString(), Platform.Access.Domain.Enums.AccountState.Active);

    private static LabPublicContent Lab(LabVisibilityState state) =>
        LabPublicContent.Create(LabId, CourseId, Guid.NewGuid(), state, "Naslov", "Cilj", Array.Empty<PublicTask>());

    private static FakeAccessService Unlocked() =>
        new(new PrerequisiteAccessResult(true, Array.Empty<Guid>()));

    private static FakeAccessService LockedBy(Guid missing) =>
        new(new PrerequisiteAccessResult(false, new[] { missing }));

    private static LabAccessController.LabAccessResponse OkBody(IActionResult result)
    {
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        return (LabAccessController.LabAccessResponse)((OkObjectResult)result).Value!;
    }

    private sealed class FixedSubjectResolver(SubjectRef? subject) : IRequestSubjectResolver
    {
        public SubjectRef? Resolve(HttpContext context) => subject;
    }

    private sealed class FixedContentReader(LabPublicContent? content) : ILabPublicContentReader
    {
        public ValueTask<LabPublicContent?> GetAsync(Guid labId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(content);
    }

    private sealed class FixedMembership(CourseRole role) : ICourseMembership
    {
        public ValueTask<CourseRole> GetRoleAsync(Guid courseId, Guid userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(role);
    }

    private sealed class FakeAccessService(PrerequisiteAccessResult result) : IPrerequisiteAccessService
    {
        public int Calls { get; private set; }

        public Task<PrerequisiteAccessResult> CheckAccessAsync(
            Guid studentId,
            Guid laboratoryId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}