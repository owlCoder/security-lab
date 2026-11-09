using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Security.RevisionAuthorization;

namespace Platform.Tests.RevisionAuthorization;

[TestFixture]
public sealed class RevisionApprovalPolicyTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _revisionId = Guid.NewGuid();

    private static AccessRequest CreateRequest(
        Guid userId,
        Guid revisionId,
        AccountState state = AccountState.Active,
        string action = RevisionApprovalPolicy.Action)
    {
        return new AccessRequest(
            new SubjectRef(userId.ToString(), state),
            action,
            new ResourceRef("revision", revisionId.ToString()),
            "test-correlation");
    }

    [Test]
    public async Task AuthorizedUser_CanApproveAnotherUsersRevision()
    {
        var authorId = Guid.NewGuid();
        var policy = CreatePolicy(authorId, hasPermission: true);

        var result = await policy.EvaluateAsync(CreateRequest(_userId, _revisionId));

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task Author_CannotApproveOwnRevision()
    {
        var policy = CreatePolicy(_userId, hasPermission: true);

        var result = await policy.EvaluateAsync(CreateRequest(_userId, _revisionId));

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UserWithoutPermission_CannotApproveRevision()
    {
        var policy = CreatePolicy(Guid.NewGuid(), hasPermission: false);

        var result = await policy.EvaluateAsync(CreateRequest(_userId, _revisionId));

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task MissingRevision_IsDenied()
    {
        var policy = CreatePolicy(null, hasPermission: true);

        var result = await policy.EvaluateAsync(CreateRequest(_userId, _revisionId));

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task InactiveUser_IsDenied()
    {
        var policy = CreatePolicy(Guid.NewGuid(), hasPermission: true);

        var result = await policy.EvaluateAsync(
            CreateRequest(_userId, _revisionId, AccountState.Blocked));

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task InvalidRevisionId_IsDenied()
    {
        var policy = CreatePolicy(Guid.NewGuid(), hasPermission: true);

        var request = new AccessRequest(
            new SubjectRef(_userId.ToString(), AccountState.Active),
            RevisionApprovalPolicy.Action,
            new ResourceRef("revision", "invalid-id"),
            "test-correlation");

        var result = await policy.EvaluateAsync(request);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task WrongAction_IsDenied()
    {
        var policy = CreatePolicy(Guid.NewGuid(), hasPermission: true);

        var result = await policy.EvaluateAsync(
            CreateRequest(_userId, _revisionId, action: "revision.delete"));

        Assert.That(result, Is.False);
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(null, false)]
    public async Task SelfApproval_FollowsCoursePolicy(bool? independent, bool expected)
    {
        var policy = CreatePolicy(_userId, true, independent);
        Assert.That(await policy.EvaluateAsync(CreateRequest(_userId, _revisionId)), Is.EqualTo(expected));
    }

    [Test]
    public async Task RelaxedPolicy_DoesNotGrantMissingApprovalPermission()
    {
        var policy = CreatePolicy(_userId, false, false);
        Assert.That(await policy.EvaluateAsync(CreateRequest(_userId, _revisionId)), Is.False);
    }

    private static RevisionApprovalPolicy CreatePolicy(Guid? authorId, bool hasPermission, bool? independent = true)
    {
        return new RevisionApprovalPolicy(
            new FakeRevisionReader(authorId, independent),
            new FakeRevisionApprovalPermission(hasPermission));
    }

    private sealed class FakeRevisionReader(Guid? authorId, bool? independent) : IRevisionResourceReader
    {
        public Task<RevisionResource?> FindAsync(
            Guid revisionId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(authorId is Guid author
                ? new RevisionResource(Guid.NewGuid(), author, independent) : null);
        }
    }

    private sealed class FakeRevisionApprovalPermission(bool hasPermission)
        : IRevisionApprovalPermission
    {
        public ValueTask<bool> CanApproveAsync(
            Guid userId,
            Guid revisionId,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(hasPermission);
        }
    }
}
