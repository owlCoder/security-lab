using Team21.Acc.Core.Common;
using Team21.Acc.Core.Groups;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Core.Interfaces;
using Team21.Acc.Infrastructure.Groups.Services;

namespace Team21.Acc.Tests.Groups;

/// <summary>
/// ACC-06 server side visibility decision, unit level. Every collaborator is a small handwritten
/// double, so these tests prove the decision rules, not the real readers or other teams' data.
/// <para>
/// Spec ACC-06: members of one group do not see the closed materials of another.
/// Spec 2.1 / SEC-01: denial happens on the server and does not reveal the resource.
/// Team 21 implementation: every denial is NotFound "Material not found." and is written to
/// <see cref="IVisibilityAuditWriter"/> with a <see cref="DenialReason"/>.
/// </para>
/// </summary>
public class MaterialVisibilityServiceTests
{
    private const string CorrelationId = "test-correlation-id";
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private Guid _courseId;
    private Guid _groupA;
    private Guid _groupB;
    private Guid _student;

    private AccountStatuses _accounts = null!;
    private CourseMemberships _courseMembers = null!;
    private CourseStates _courseStates = null!;
    private AccessRules _rules = null!;
    private GroupMemberships _groups = null!;
    private RecordingVisibilityAuditWriter _audit = null!;

    [SetUp]
    public void FreshWorld()
    {
        _courseId = Guid.NewGuid();
        _groupA = Guid.NewGuid();
        _groupB = Guid.NewGuid();
        _student = Guid.NewGuid();

        _accounts = new AccountStatuses();
        _courseMembers = new CourseMemberships();
        _courseStates = new CourseStates();
        _rules = new AccessRules();
        _groups = new GroupMemberships();
        _audit = new RecordingVisibilityAuditWriter();

        _courseStates.Set(_courseId, CourseState.Active);
    }

    // 1. Primary: ACC-06 (positive scenario).
    [Test]
    public async Task CanAccess_StudentInAllowedGroup_Succeeds()
    {
        GivenActiveStudentOfCourse(_student);
        _groups.Add(_student, _courseId, _groupA);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var service = CreateService(_student);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_audit.Denials, Is.Empty);
    }

    // 2. Primary: ACC-06 (negative scenario, no disclosure). Related: ASN-05, SEC-01.
    [Test]
    public async Task CanAccess_StudentInOtherGroup_ReturnsNotFound()
    {
        GivenActiveStudentOfCourse(_student);
        _groups.Add(_student, _courseId, _groupB);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var unknownMaterial = new MaterialRef(MaterialType.Assignment, Guid.NewGuid());
        var service = CreateService(_student);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);
        var unknownResult = await service.CanAccessAsync(unknownMaterial, MaterialAction.Read);

        AssertMaterialNotFound(result, material);
        // Not disclosing: a closed material answers exactly like a material that does not exist
        Assert.That(result.Error, Is.EqualTo(unknownResult.Error));
        // The reason stays inside the audit trail, not in the response
        Assert.That(_audit.Denials, Has.Count.EqualTo(2));
        AssertDeniedWith(_audit.Denials[0], material, _courseId, DenialReason.NotInGroup);
        AssertDeniedWith(_audit.Denials[1], unknownMaterial, courseId: null, DenialReason.NoRule);
    }

    // 3. Primary: ACC-06 (enrolled, but no group).
    [Test]
    public async Task CanAccess_StudentWithoutGroup_ReturnsNotFound()
    {
        GivenActiveStudentOfCourse(_student);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var service = CreateService(_student);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);

        AssertMaterialNotFound(result, material);
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        AssertDeniedWith(_audit.Denials[0], material, _courseId, DenialReason.NotInGroup);
    }

    // 4. Primary: ACC-06 (positive scenario). Team 21 model: CourseWide is visible to every active
    // member of the course.
    [Test]
    public async Task CanAccess_CourseWideMaterial_EnrolledStudentSucceeds()
    {
        GivenActiveStudentOfCourse(_student);
        var material = GivenRule(MaterialVisibility.CourseWide);
        var service = CreateService(_student);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_audit.Denials, Is.Empty);
    }

    // 5. Primary: ACC-06. Related: ACC-05 default deny, applied by Team 21 to a restricted material
    // nobody was granted.
    [Test]
    public async Task CanAccess_RestrictedMaterialWithoutAllowedGroups_DeniesStudent()
    {
        GivenActiveStudentOfCourse(_student);
        _groups.Add(_student, _courseId, _groupA);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups);
        var service = CreateService(_student);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);

        AssertMaterialNotFound(result, material);
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        AssertDeniedWith(_audit.Denials[0], material, _courseId, DenialReason.NotInGroup);
    }

    // 6. Primary: ACC-06. Related: SEC-01 / 2.1, no caller, no access. 401 is the Team 21 mapping of
    // Unauthorized.
    [Test]
    public async Task CanAccess_AnonymousUser_ReturnsUnauthorized()
    {
        var material = GivenRule(MaterialVisibility.CourseWide);
        var service = CreateService(userId: null);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.Unauthorized));
        // Result carries no value; the only text going out must not name the material
        Assert.That(result.Error.Message, Does.Not.Contain(material.MaterialId.ToString()));
        Assert.That(result.Error.Message, Does.Not.Contain(_courseId.ToString()));
    }

    // 7. Primary: ACC-06. Related: 2.1 (account status is part of the decision); the status itself is an
    // IDN-03 dependency read through a double. Team 21: checked before the rule lookup, so a blocked
    // caller cannot learn whether a material exists.
    [Test]
    public async Task CanAccess_BlockedAccount_ReturnsNotFound()
    {
        _accounts.Set(_student, AccountStatus.Blocked);
        _courseMembers.Set(_student, new CourseMembership(_courseId, CourseRole.Student, Active: true, CanReview: false));
        _groups.Add(_student, _courseId, _groupA);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var service = CreateService(_student);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);

        AssertMaterialNotFound(result, material);
        Assert.That(_rules.Lookups, Is.Zero, "the material rule must not be read for a blocked account");
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        // The rule was never read, so the course is not known to the denial
        AssertDeniedWith(_audit.Denials[0], material, courseId: null, DenialReason.AccountNotActive);
    }

    // 8. Primary: ACC-06. Related: SEC-01 (IDOR). The course comes from the stored rule, never from
    // the caller, so even a course-wide material of another course stays closed.
    [Test]
    public async Task CanAccess_MaterialFromDifferentCourse_ReturnsNotFound()
    {
        var otherCourse = Guid.NewGuid();
        _courseStates.Set(otherCourse, CourseState.Active);
        GivenActiveStudentOfCourse(_student);
        var material = GivenRuleInCourse(otherCourse, MaterialVisibility.CourseWide);
        var service = CreateService(_student);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);

        AssertMaterialNotFound(result, material);
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        AssertDeniedWith(_audit.Denials[0], material, otherCourse, DenialReason.NotCourseMember);
    }

    // 9. Primary: ACC-06. Related: ACC-03 (a change of rights applies on the next request). The same
    // service instance is reused, so a cached decision would show up as a second success.
    [Test]
    public async Task CanAccess_GroupMembershipRemovedBetweenRequests_RevokesAccess()
    {
        GivenActiveStudentOfCourse(_student);
        _groups.Add(_student, _courseId, _groupA);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var service = CreateService(_student);

        var before = await service.CanAccessAsync(material, MaterialAction.Read);
        _groups.Remove(_student, _groupA);
        var after = await service.CanAccessAsync(material, MaterialAction.Read);

        Assert.That(before.IsSuccess, Is.True);
        AssertMaterialNotFound(after, material);
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        AssertDeniedWith(_audit.Denials[0], material, _courseId, DenialReason.NotInGroup);
    }

    // 10. Primary: ACC-06. Related: ACC-03, SEC-01; the course membership is an ACC-02/03 dependency
    // read through a double. Course-wide material, so only the course membership decides.
    [Test]
    public async Task CanAccess_CourseMembershipRevokedBetweenRequests_RevokesAccess()
    {
        GivenActiveStudentOfCourse(_student);
        var material = GivenRule(MaterialVisibility.CourseWide);
        var service = CreateService(_student);

        var before = await service.CanAccessAsync(material, MaterialAction.Read);
        _courseMembers.Set(_student, new CourseMembership(_courseId, CourseRole.Student, Active: false, CanReview: false));
        var after = await service.CanAccessAsync(material, MaterialAction.Read);

        Assert.That(before.IsSuccess, Is.True);
        AssertMaterialNotFound(after, material);
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        AssertDeniedWith(_audit.Denials[0], material, _courseId, DenialReason.NotCourseMember);
    }

    // 11. Primary: ACC-06. Related: ACC-05, an action without a policy gives no access. The caller
    // would otherwise be allowed (course-wide material, active student), so only the action can cause
    // the denial.
    [Test]
    public async Task CanAccess_UnknownAction_ReturnsNotFound()
    {
        const MaterialAction unknownAction = (MaterialAction)999;
        GivenActiveStudentOfCourse(_student);
        var material = GivenRule(MaterialVisibility.CourseWide);
        var service = CreateService(_student);

        var result = await service.CanAccessAsync(material, unknownAction);

        AssertMaterialNotFound(result, material);
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        AssertDeniedWith(_audit.Denials[0], material, courseId: null, DenialReason.UnknownAction, unknownAction);
    }

    // 12. Primary: ACC-06 (list view).
    [Test]
    public async Task FilterVisible_StudentSeesOnlyCourseWideAndOwnGroupMaterials()
    {
        GivenActiveStudentOfCourse(_student);
        _groups.Add(_student, _courseId, _groupA);
        var courseWide = GivenRule(MaterialVisibility.CourseWide);
        var forGroupA = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var forGroupB = GivenRule(MaterialVisibility.RestrictedToGroups, _groupB);
        var forNobody = GivenRule(MaterialVisibility.RestrictedToGroups);
        var service = CreateService(_student);

        var result = await service.FilterVisibleAsync(_courseId);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EquivalentTo(new[] { courseWide, forGroupA }));
        Assert.That(result.Value, Does.Not.Contain(forGroupB).And.Not.Contain(forNobody));
    }

    // 13. Primary: ACC-06. Related: SEC-01. Defense in depth against an inconsistent reader, not
    // normal reader behavior: the foreign rule is course-wide, so only the service's own course check
    // can remove it.
    [Test]
    public async Task FilterVisible_CrossCourseRulesAreExcluded()
    {
        var otherCourse = Guid.NewGuid();
        GivenActiveStudentOfCourse(_student);
        var own = new MaterialRef(MaterialType.Assignment, Guid.NewGuid());
        var foreign = new MaterialRef(MaterialType.Assignment, Guid.NewGuid());
        var leakingReader = new ForeignCourseLeakingRuleReader(
            new MaterialAccessRule(own, _courseId, MaterialVisibility.CourseWide, []),
            new MaterialAccessRule(foreign, otherCourse, MaterialVisibility.CourseWide, []));
        var service = CreateService(_student, leakingReader);

        var result = await service.FilterVisibleAsync(_courseId);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EquivalentTo(new[] { own }));
    }

    // 14. Primary: ACC-06 (list view). Related: SEC-01, no caller, no list.
    [Test]
    public async Task FilterVisible_Anonymous_ReturnsUnauthorized()
    {
        GivenRule(MaterialVisibility.CourseWide);
        var service = CreateService(userId: null);

        var result = await service.FilterVisibleAsync(_courseId);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.Unauthorized));
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Error.Message, Does.Not.Contain(_courseId.ToString()));
    }

    // 15. Primary: ACC-06 (list view). Related: SEC-01. The course has a course-wide material, so an
    // empty list would not prove anything; the caller must get no list at all.
    [Test]
    public async Task FilterVisible_StudentNotEnrolled_ReturnsNotFound()
    {
        _accounts.Set(_student, AccountStatus.Active);
        GivenRule(MaterialVisibility.CourseWide);
        var service = CreateService(_student);

        var result = await service.FilterVisibleAsync(_courseId);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.NotFound));
        Assert.That(result.Error.Message, Is.EqualTo("Course not found."));
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        AssertDeniedWith(_audit.Denials[0], material: null, _courseId, DenialReason.NotCourseMember, action: null);
    }

    // 16 (V01). Primary: ACC-06. Related: ACC-03 (teacher of the course). Team 21: a teacher sees
    // every material of an active course, so the group restriction does not apply. The teacher is in
    // no group, so only the teacher role can grant access.
    [Test]
    public async Task CanAccess_TeacherOutsideAllowedGroup_Succeeds()
    {
        var teacher = Guid.NewGuid();
        GivenActiveMemberOfCourse(teacher, CourseRole.Teacher, canReview: false);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var service = CreateService(teacher);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_audit.Denials, Is.Empty);
    }

    // 17 (V02). Primary: ACC-06. Related: ACC-03, spec role "assistant": only teacher actions the
    // teacher explicitly delegated. Team 21: the delegated review right (CanReview) gives the teacher's
    // view of the course. The assistant is in no group, so only CanReview can grant access.
    [Test]
    public async Task CanAccess_ReviewingAssistantOutsideAllowedGroup_Succeeds()
    {
        var assistant = Guid.NewGuid();
        GivenActiveMemberOfCourse(assistant, CourseRole.Assistant, canReview: true);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var service = CreateService(assistant);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_audit.Denials, Is.Empty);
    }

    // 18 (V03). Primary: ACC-06. Related: ACC-08 (a role alone does not bypass the resource rule),
    // SEC-01. Without delegated review an assistant is held to the group rule like a student. The
    // assistant is an active member of an active course and belongs to a group, just not an allowed
    // one, so the group check is the only reason for the denial.
    [Test]
    public async Task CanAccess_AssistantWithoutReviewRight_ReturnsNotFound()
    {
        var assistant = Guid.NewGuid();
        GivenActiveMemberOfCourse(assistant, CourseRole.Assistant, canReview: false);
        _groups.Add(assistant, _courseId, _groupB);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var service = CreateService(assistant);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);

        AssertMaterialNotFound(result, material);
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        AssertDeniedWith(_audit.Denials[0], material, _courseId, DenialReason.NotInGroup, actorId: assistant);
    }

    // 19 (V04). Primary: ACC-06. Related: ACC-01 (archived course), ACC-03 (a change applies on the
    // next request). The same service instance first grants access, so the membership and the group
    // grant are valid and only the archived state can cause the denial; a cached decision would show
    // up as a second success.
    [Test]
    public async Task CanAccess_StudentInAllowedGroupOfArchivedCourse_ReturnsNotFound()
    {
        GivenActiveStudentOfCourse(_student);
        _groups.Add(_student, _courseId, _groupA);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var service = CreateService(_student);

        var beforeArchive = await service.CanAccessAsync(material, MaterialAction.Read);
        _courseStates.Set(_courseId, CourseState.Archived);
        var afterArchive = await service.CanAccessAsync(material, MaterialAction.Read);

        Assert.That(beforeArchive.IsSuccess, Is.True);
        AssertMaterialNotFound(afterArchive, material);
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        AssertDeniedWith(_audit.Denials[0], material, _courseId, DenialReason.CourseArchived);
    }

    // 20 (V05). Primary: ACC-06. Related: ACC-01 (archiving keeps the history). Team 21: in an archived
    // course the teacher and a reviewing assistant still see the materials. The material is restricted
    // to a group the caller is not in, so access needs both the archived-course exception and the
    // privileged-viewer rule.
    [TestCase(CourseRole.Teacher, false, TestName = "CanAccess_ArchivedCourse_TeacherRetainsAccess")]
    [TestCase(CourseRole.Assistant, true, TestName = "CanAccess_ArchivedCourse_ReviewingAssistantRetainsAccess")]
    public async Task CanAccess_ArchivedCourse_PrivilegedViewerRetainsAccess(CourseRole role, bool canReview)
    {
        var member = Guid.NewGuid();
        GivenActiveMemberOfCourse(member, role, canReview);
        _courseStates.Set(_courseId, CourseState.Archived);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var service = CreateService(member);

        var result = await service.CanAccessAsync(material, MaterialAction.Read);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_audit.Denials, Is.Empty);
    }

    // 21 (V06). Primary: ACC-06 (list view). Related: ACC-01 (archived course), ACC-03 (a change applies
    // on the next request), SEC-01. The list operation checks the course state on its own, so this
    // covers that check rather than the one in CanAccess. The first list shows the grant is valid; the
    // same service instance then answers like for a missing course, so a cached decision would show up.
    [Test]
    public async Task FilterVisible_StudentInArchivedCourse_ReturnsNotFound()
    {
        GivenActiveStudentOfCourse(_student);
        _groups.Add(_student, _courseId, _groupA);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var service = CreateService(_student);

        var beforeArchive = await service.FilterVisibleAsync(_courseId);
        _courseStates.Set(_courseId, CourseState.Archived);
        var afterArchive = await service.FilterVisibleAsync(_courseId);

        Assert.That(beforeArchive.IsSuccess, Is.True);
        Assert.That(beforeArchive.Value, Is.EquivalentTo(new[] { material }));
        Assert.That(afterArchive.IsSuccess, Is.False);
        Assert.That(afterArchive.Value, Is.Null);
        Assert.That(afterArchive.Error!.Type, Is.EqualTo(ErrorType.NotFound));
        Assert.That(afterArchive.Error.Message, Is.EqualTo("Course not found."));
        Assert.That(afterArchive.Error.Message, Does.Not.Contain(_courseId.ToString()));
        Assert.That(afterArchive.Error.Message, Does.Not.Contain(material.MaterialId.ToString()));
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        AssertDeniedWith(_audit.Denials[0], material: null, _courseId, DenialReason.CourseArchived, action: null);
    }

    // 22 (V07). Primary: ACC-06. Related: ACC-01 (archived course). Only the teacher and a reviewing
    // assistant keep access to an archived course. The assistant is in the allowed group, so the
    // first request succeeds and only the archived state can cause the denial, not the group rule.
    [Test]
    public async Task CanAccess_AssistantWithoutReviewRight_ArchivedCourse_ReturnsNotFound()
    {
        var assistant = Guid.NewGuid();
        GivenActiveMemberOfCourse(assistant, CourseRole.Assistant, canReview: false);
        _groups.Add(assistant, _courseId, _groupA);
        var material = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var service = CreateService(assistant);

        var beforeArchive = await service.CanAccessAsync(material, MaterialAction.Read);
        _courseStates.Set(_courseId, CourseState.Archived);
        var afterArchive = await service.CanAccessAsync(material, MaterialAction.Read);

        Assert.That(beforeArchive.IsSuccess, Is.True);
        AssertMaterialNotFound(afterArchive, material);
        Assert.That(_audit.Denials, Has.Count.EqualTo(1));
        AssertDeniedWith(_audit.Denials[0], material, _courseId, DenialReason.CourseArchived, actorId: assistant);
    }

    // 23 (V08). Primary: ACC-06 (list view). Team 21: the teacher and a reviewing assistant see every
    // material of the course in the list. The caller is in no group, so only the list operation's
    // privileged-viewer rule can put the restricted material in the list. A material of another
    // course must not appear.
    [TestCase(CourseRole.Teacher, false, TestName = "FilterVisible_Teacher_SeesRestrictedMaterial")]
    [TestCase(CourseRole.Assistant, true, TestName = "FilterVisible_ReviewingAssistant_SeesRestrictedMaterial")]
    public async Task FilterVisible_PrivilegedViewer_SeesRestrictedMaterial(CourseRole role, bool canReview)
    {
        var member = Guid.NewGuid();
        var otherCourse = Guid.NewGuid();
        _courseStates.Set(otherCourse, CourseState.Active);
        GivenActiveMemberOfCourse(member, role, canReview);
        var restricted = GivenRule(MaterialVisibility.RestrictedToGroups, _groupA);
        var foreign = GivenRuleInCourse(otherCourse, MaterialVisibility.CourseWide);
        var service = CreateService(member);

        var result = await service.FilterVisibleAsync(_courseId);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EquivalentTo(new[] { restricted }));
        Assert.That(result.Value, Does.Not.Contain(foreign));
        Assert.That(_audit.Denials, Is.Empty);
    }

    private MaterialVisibilityService CreateService(Guid? userId, IMaterialAccessRuleReader? rules = null) =>
        new(
            new StubCurrentActor(userId),
            new FixedCorrelationContext(CorrelationId),
            _accounts,
            _courseMembers,
            _courseStates,
            rules ?? _rules,
            _groups,
            _audit,
            new FixedTimeProvider(Now));

    private void GivenActiveStudentOfCourse(Guid userId) =>
        GivenActiveMemberOfCourse(userId, CourseRole.Student, canReview: false);

    private void GivenActiveMemberOfCourse(Guid userId, CourseRole role, bool canReview)
    {
        _accounts.Set(userId, AccountStatus.Active);
        _courseMembers.Set(userId, new CourseMembership(_courseId, role, Active: true, CanReview: canReview));
    }

    private MaterialRef GivenRule(MaterialVisibility visibility, params Guid[] allowedGroups) =>
        GivenRuleInCourse(_courseId, visibility, allowedGroups);

    private MaterialRef GivenRuleInCourse(Guid courseId, MaterialVisibility visibility, params Guid[] allowedGroups)
    {
        var material = new MaterialRef(MaterialType.Assignment, Guid.NewGuid());
        _rules.Set(new MaterialAccessRule(material, courseId, visibility, allowedGroups));
        return material;
    }

    private static void AssertMaterialNotFound(Result result, MaterialRef material)
    {
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.NotFound));
        Assert.That(result.Error.Message, Is.EqualTo("Material not found."));
        Assert.That(result.Error.Message, Does.Not.Contain(material.MaterialId.ToString()));
    }

    // material and action are null when a course's material list was denied; the actor is the
    // student unless given
    private void AssertDeniedWith(
        VisibilityDenial denial, MaterialRef? material, Guid? courseId, DenialReason reason,
        MaterialAction? action = MaterialAction.Read, Guid? actorId = null)
    {
        Assert.Multiple(() =>
        {
            Assert.That(denial.ActorId, Is.EqualTo(actorId ?? _student));
            Assert.That(denial.CourseId, Is.EqualTo(courseId));
            Assert.That(denial.Material, Is.EqualTo(material));
            Assert.That(denial.Action, Is.EqualTo(action));
            Assert.That(denial.Reason, Is.EqualTo(reason));
            Assert.That(denial.OccurredAt, Is.EqualTo(Now.UtcDateTime));
            Assert.That(denial.CorrelationId, Is.EqualTo(CorrelationId));
        });
    }

    // Test doubles: one small class per interface the service depends on

    private sealed class StubCurrentActor(Guid? userId) : ICurrentActor
    {
        public Guid? UserId { get; } = userId;
        public bool IsAuthenticated => UserId is not null;
    }

    private sealed class FixedCorrelationContext(string correlationId) : ICorrelationContext
    {
        public string CorrelationId { get; } = correlationId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Unknown accounts are inactive, like the deny-all default
    private sealed class AccountStatuses : IAccountStatusReader
    {
        private readonly Dictionary<Guid, AccountStatus> _statuses = new();

        public void Set(Guid accountId, AccountStatus status) => _statuses[accountId] = status;

        public Task<AccountStatus> GetStatusAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult(_statuses.GetValueOrDefault(accountId, AccountStatus.Inactive));
    }

    private sealed class CourseMemberships : ICourseMembershipReader
    {
        private readonly Dictionary<(Guid, Guid), CourseMembership> _memberships = new();

        public void Set(Guid accountId, CourseMembership membership) =>
            _memberships[(accountId, membership.CourseId)] = membership;

        public Task<CourseMembership?> FindAsync(Guid accountId, Guid courseId, CancellationToken ct = default) =>
            Task.FromResult(_memberships.GetValueOrDefault((accountId, courseId)));
    }

    private sealed class CourseStates : ICourseStateReader
    {
        private readonly Dictionary<Guid, CourseState> _states = new();

        public void Set(Guid courseId, CourseState state) => _states[courseId] = state;

        public Task<CourseState?> GetStateAsync(Guid courseId, CancellationToken ct = default) =>
            Task.FromResult(_states.TryGetValue(courseId, out var state) ? state : (CourseState?)null);
    }

    private sealed class AccessRules : IMaterialAccessRuleReader
    {
        private readonly Dictionary<MaterialRef, MaterialAccessRule> _rules = new();

        public int Lookups { get; private set; }

        public void Set(MaterialAccessRule rule) => _rules[rule.Material] = rule;

        public Task<MaterialAccessRule?> FindAsync(MaterialRef material, CancellationToken ct = default)
        {
            Lookups++;
            return Task.FromResult(_rules.GetValueOrDefault(material));
        }

        public Task<IReadOnlyList<MaterialAccessRule>> ListByCourseAsync(Guid courseId, CancellationToken ct = default)
        {
            Lookups++;
            return Task.FromResult<IReadOnlyList<MaterialAccessRule>>(
                _rules.Values.Where(r => r.CourseId == courseId).ToList());
        }
    }

    private sealed class GroupMemberships : IGroupMembershipReader
    {
        private readonly List<(Guid AccountId, GroupMembership Membership)> _memberships = new();

        public void Add(Guid accountId, Guid courseId, Guid groupId) =>
            _memberships.Add((accountId, new GroupMembership(groupId, "group", courseId, Now.UtcDateTime)));

        public void Remove(Guid accountId, Guid groupId) =>
            _memberships.RemoveAll(m => m.AccountId == accountId && m.Membership.GroupId == groupId);

        public Task<IReadOnlyList<GroupMembership>> GroupsOfAsync(Guid accountId, Guid courseId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GroupMembership>>(
                _memberships
                    .Where(m => m.AccountId == accountId && m.Membership.CourseId == courseId)
                    .Select(m => m.Membership)
                    .ToList());
    }

    /// <summary>
    /// Defense in depth only. Breaks the reader contract on purpose: ListByCourseAsync returns every
    /// rule it holds, including rules of other courses. The real MaterialAccessRuleReader filters
    /// by course; this double checks that the service does not rely on that alone.
    /// </summary>
    private sealed class ForeignCourseLeakingRuleReader(params MaterialAccessRule[] rules) : IMaterialAccessRuleReader
    {
        public Task<MaterialAccessRule?> FindAsync(MaterialRef material, CancellationToken ct = default) =>
            Task.FromResult(rules.FirstOrDefault(r => r.Material == material));

        public Task<IReadOnlyList<MaterialAccessRule>> ListByCourseAsync(Guid courseId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MaterialAccessRule>>(rules);
    }

    private sealed class RecordingVisibilityAuditWriter : IVisibilityAuditWriter
    {
        public List<VisibilityDenial> Denials { get; } = new();

        public Task WriteDenialAsync(VisibilityDenial denial, CancellationToken ct = default)
        {
            Denials.Add(denial);
            return Task.CompletedTask;
        }
    }
}
