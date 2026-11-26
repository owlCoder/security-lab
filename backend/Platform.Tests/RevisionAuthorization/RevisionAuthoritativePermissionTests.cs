using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Membership;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Security.RevisionAuthorization;
using IAccessDecision = Platform.Access.Contracts.IAccessDecision;

namespace Platform.Tests.RevisionAuthorization;

// Exercise the production permission reader and policy registration together. EF InMemory
// isolates these authorization tests; PostgreSQL/HTTP behavior has separate integration tests.
public sealed class RevisionAuthoritativePermissionTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Course = Guid.NewGuid();
    private static readonly Guid Revision = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [TestCase(CourseRole.Unknown, "missing")]
    [TestCase(CourseRole.Teacher, "missing")]
    [TestCase(CourseRole.Assistant, "missing")]
    [TestCase(CourseRole.Teacher, "revoked")]
    [TestCase(CourseRole.Assistant, "revoked")]
    [TestCase(CourseRole.Teacher, "expired")]
    [TestCase(CourseRole.Assistant, "expired")]
    [TestCase(CourseRole.Assistant, "other-course")]
    public async Task MissingValidGrant_DeniesApprovalDespiteAdministrationOrCourseRole(
        CourseRole role, string grantState)
    {
        var audit = new RecordingAudit();
        await using var services = CreateServices(role, audit);
        await SetGrant(services, grantState);

        Assert.That(services.GetRequiredService<IAdminDirectory>().IsAdministrator(User), Is.True);
        var decision = await Decide(services);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
            Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyDenied));
            Assert.That(audit.Writes, Has.Count.EqualTo(1));
            Assert.That(audit.Writes.Single().Request.Action, Is.EqualTo(RevisionActions.Approve));
            Assert.That(audit.Writes.Single().Decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        });
    }

    [Test]
    public async Task RevocationAndExpiry_TakeEffectWithoutFallingBackToAssistantDelegation()
    {
        var audit = new RecordingAudit();
        await using var services = CreateServices(CourseRole.Assistant, audit);
        await SetGrant(services, "active");
        Assert.That((await Decide(services)).Outcome, Is.EqualTo(AccessOutcome.Allow));

        await using (var db = await services.GetRequiredService<IDbContextFactory<RevisionSecurityDbContext>>().CreateDbContextAsync())
        {
            var grant = await db.Permissions.SingleAsync();
            grant.IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.That((await Decide(services)).Outcome, Is.EqualTo(AccessOutcome.Deny));

        await using (var db = await services.GetRequiredService<IDbContextFactory<RevisionSecurityDbContext>>().CreateDbContextAsync())
        {
            var grant = await db.Permissions.SingleAsync();
            grant.IsActive = true;
            grant.ExpiresAt = Now; // Expiry is exclusive: equality is already expired.
            await db.SaveChangesAsync();
        }
        Assert.That((await Decide(services)).Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    private static ServiceProvider CreateServices(CourseRole role, RecordingAudit audit)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedClock());
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContextFactory<RevisionSecurityDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IRevisionCoursePermissionReader, PostgresRevisionCoursePermissionReader>();
        var memberships = new InMemoryMembershipReader();
        memberships.Set(new CourseMembership(User, Course, role, MembershipStatus.Active,
            new HashSet<string>(StringComparer.Ordinal) { RevisionActions.Approve }));
        services.AddSingleton<IMembershipReader>(memberships);
        services.AddSingleton<IAdminDirectory>(new Administrator());
        services.AddSingleton<IRevisionResourceReader>(new Resource());
        services.AddSingleton<IRevisionAuthorReader>(new AuthorReader());
        services.AddSingleton<IAccessAuditWriter>(audit);
        services.AddRevisionAuthorization();
        services.AddRevisionAccessPolicies();
        services.AddAccessControl();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static async Task SetGrant(ServiceProvider services, string state)
    {
        if (state == "missing") return;
        await using var db = await services.GetRequiredService<IDbContextFactory<RevisionSecurityDbContext>>().CreateDbContextAsync();
        db.Permissions.Add(new RevisionCoursePermission
        {
            UserId = User, CourseId = state == "other-course" ? Guid.NewGuid() : Course,
            Action = RevisionActions.Approve, IsActive = state != "revoked",
            ExpiresAt = state == "expired" ? Now : Now.AddHours(1)
        });
        await db.SaveChangesAsync();
    }

    private static ValueTask<AccessDecision> Decide(ServiceProvider services) =>
        services.GetRequiredService<IAccessDecision>().DecideAsync(new AccessRequest(
            new SubjectRef(User.ToString("D"), AccountState.Active), RevisionActions.Approve,
            new ResourceRef("revision", Revision.ToString("D")), "rev05-authoritative-grant"));

    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Administrator : IAdminDirectory { public bool IsAdministrator(Guid id) => id == User; }
    private sealed class AuthorReader : IRevisionAuthorReader
    {
        public Task<Guid?> FindAuthorIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(id == Revision ? Author : null);
    }
    private sealed class Resource : IRevisionResourceReader
    {
        public Task<RevisionResource?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<RevisionResource?>(id == Revision ? new RevisionResource(Course, Author, true) : null);
    }
    private sealed class RecordingAudit : IAccessAuditWriter
    {
        public List<(AccessRequest Request, AccessDecision Decision)> Writes { get; } = [];
        public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default)
        {
            if (decision.Outcome == AccessOutcome.Deny) Writes.Add((request, decision));
            return ValueTask.CompletedTask;
        }
    }
}
