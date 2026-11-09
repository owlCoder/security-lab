using Microsoft.AspNetCore.Http;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Middleware;
using Platform.Api.Models.Identity;
using Platform.Api.Security.RevisionAuthorization;
using Platform.Api.Services.Identity;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Enums;

namespace Platform.Tests.RevisionAuthorization;

public sealed class RevisionRequestIdentityTests
{
    private static readonly Guid User = Guid.NewGuid();

    [Test]
    public async Task RevisionIdentity_UsesValidatedCookie_AndSameSessionInUseCase()
    {
        var http = Request("/api/revisions/mine", "valid");
        http.Request.Headers["X-Actor-ID"] = Guid.NewGuid().ToString();
        var source = new Fallback();
        var provider = new RevisionSessionSubjectProvider(source, new Sessions(), new Accounts());
        Assert.That(await provider.GetSubjectAsync(http), Is.EqualTo(new SubjectRef(User.ToString(), AccountState.Active)));
        Assert.That(source.Calls, Is.Zero);

        // The existing Identity middleware revalidates and stores its own session.
        http.Items[SessionCheckMiddleware.SessionItemKey] = http.Items[RevisionRequestIdentity.SessionItemKey];
        Assert.That(new RevisionSubjectResolver(source).Resolve(http)?.Id, Is.EqualTo(User.ToString()));
    }

    [TestCase(null)]
    [TestCase("revoked")]
    [TestCase("pending")]
    public async Task MissingRevokedOrMfaPendingCookie_CannotFallBackToDevelopmentIdentity(string? token)
    {
        var http = Request("/api/revisions/mine", token);
        var source = new Fallback();
        var provider = new RevisionSessionSubjectProvider(source, new Sessions(), new Accounts());
        Assert.That(await provider.GetSubjectAsync(http), Is.Null);
        Assert.That(new RevisionSubjectResolver(source).Resolve(http), Is.Null);
        Assert.That(source.Calls, Is.Zero);
    }

    [Test]
    public async Task OtherRoutes_KeepOriginalIdentityProvider()
    {
        var http = Request("/api/other", null);
        var source = new Fallback();
        var provider = new RevisionSessionSubjectProvider(source, new Sessions(), new Accounts());
        Assert.That((await provider.GetSubjectAsync(http))?.Id, Is.EqualTo("existing-provider"));
        Assert.That(new RevisionSubjectResolver(source).Resolve(http)?.Id, Is.EqualTo("existing-provider"));
        Assert.That(source.Calls, Is.EqualTo(2));
    }

    private static DefaultHttpContext Request(string path, string? token)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        if (token is not null) http.Request.Headers.Cookie = $"{SessionCookie.Name}={token}";
        return http;
    }

    private sealed class Fallback : IAccessSubjectProvider, IRequestSubjectResolver
    {
        public int Calls { get; private set; }
        public SubjectRef? Resolve(HttpContext context)
        {
            Calls++;
            return new SubjectRef("existing-provider", AccountState.Active);
        }
        public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Resolve(context));
    }

    private sealed class Sessions : ISessionManager
    {
        public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default) =>
            Task.FromResult<ValidateSession?>(token switch
            {
                "valid" => new ValidateSession(Guid.NewGuid(), User, AuthenticationLevel.Active),
                "pending" => new ValidateSession(Guid.NewGuid(), User, AuthenticationLevel.MfaPending),
                _ => null
            });
        public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Accounts : IAccountStatusReader
    {
        public Task<AccountSnapshot?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountSnapshot?>(new AccountSnapshot(id, AccountStatus.Active));
    }
}
