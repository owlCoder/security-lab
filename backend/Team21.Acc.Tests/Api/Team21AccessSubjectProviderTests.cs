using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Team21.Integration;
using Team21.Acc.Api;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Core.Interfaces;

namespace Team21.Acc.Tests.Api;

public class Team21AccessSubjectProviderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [TestCase(AccountStatus.Active, AccountState.Active)]
    [TestCase(AccountStatus.Blocked, AccountState.Blocked)]
    [TestCase(AccountStatus.Inactive, AccountState.Unknown)]
    [TestCase((AccountStatus)99, AccountState.Unknown)]
    public async Task AccountStateIsExplicitlyMapped(AccountStatus status, AccountState expected)
    {
        var accounts = new Accounts { Status = status };
        var inner = new OriginalProvider(new DisposalCounter());
        var subject = await new Team21AccessSubjectProvider(inner, () => new Actor(UserId), () => accounts)
            .GetSubjectAsync(Context(marked: true));
        Assert.Multiple(() =>
        {
            Assert.That(subject, Is.EqualTo(new SubjectRef(UserId.ToString("D"), expected)));
            Assert.That(accounts.LastId, Is.EqualTo(UserId));
            Assert.That(inner.Calls, Is.Zero);
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task MissingOrUnauthenticatedActorHasNoFallback(bool authenticated, bool empty)
    {
        var accounts = new Accounts();
        var inner = new OriginalProvider(new DisposalCounter());
        var actor = new Actor(empty ? Guid.Empty : null) { IsAuthenticated = authenticated };
        var subject = await new Team21AccessSubjectProvider(inner, () => actor, () => accounts)
            .GetSubjectAsync(Context(marked: true));
        Assert.Multiple(() =>
        {
            Assert.That(subject, Is.Null);
            Assert.That(accounts.Calls, Is.Zero);
            Assert.That(inner.Calls, Is.Zero);
        });
    }

    [TestCase("X-Test-User")]
    [TestCase("X-Stub-User")]
    [TestCase("X-User-Id")]
    public async Task HeadersCannotCreateAnActor(string header)
    {
        var http = Context(marked: true);
        http.Request.Headers[header] = UserId.ToString();
        var accounts = new Accounts();
        var subject = await new Team21AccessSubjectProvider(
            new OriginalProvider(new DisposalCounter()), () => new Actor(null), () => accounts).GetSubjectAsync(http);
        Assert.That(subject, Is.Null);
        Assert.That(accounts.Calls, Is.Zero);
    }

    [Test]
    public async Task OtherEndpointsUseTheOriginalProviderWithoutAccountLookup()
    {
        var inner = new OriginalProvider(new DisposalCounter());
        var accounts = new Accounts { Throw = true };
        var subject = await new Team21AccessSubjectProvider(inner, () => new Actor(UserId), () => accounts)
            .GetSubjectAsync(Context(marked: false));
        Assert.That(subject, Is.SameAs(inner.Subject));
        Assert.That(inner.Calls, Is.EqualTo(1));
        Assert.That(accounts.Calls, Is.Zero);
    }

    [Test]
    public void UnavailableAccountReaderCannotReturnAnActiveSubject()
    {
        var subject = new Team21AccessSubjectProvider(
            new OriginalProvider(new DisposalCounter()), () => new Actor(UserId), () => new Accounts { Throw = true });
        Assert.ThrowsAsync<InvalidOperationException>(() => subject.GetSubjectAsync(Context(true)).AsTask());
    }

    [Test]
    public async Task OtherEndpointsDoNotEvenResolveTeam21Dependencies()
    {
        var inner = new OriginalProvider(new DisposalCounter());
        var provider = new Team21AccessSubjectProvider(inner,
            () => throw new InvalidOperationException("Team 21 actor must not be resolved"),
            () => throw new InvalidOperationException("Team 21 accounts must not be resolved"));
        Assert.That(await provider.GetSubjectAsync(Context(false)), Is.SameAs(inner.Subject));
    }

    [TestCase(ServiceLifetime.Scoped)]
    [TestCase(ServiceLifetime.Singleton)]
    public async Task DecorationPreservesOriginalLifetimeAndDisposal(ServiceLifetime lifetime)
    {
        var disposals = new DisposalCounter();
        var services = Services();
        services.AddSingleton(disposals);
        services.Add(new ServiceDescriptor(typeof(IAccessSubjectProvider), typeof(OriginalProvider), lifetime));
        services.AddTeam21SessionAccess();
        services.AddTeam21SessionAccess();
        Assert.That(services.Count(d => d.ServiceType == typeof(IAccessSubjectProvider) && !d.IsKeyedService), Is.EqualTo(1));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        try
        {
            SubjectRef? first;
            using (var scope = provider.CreateScope())
            {
                var adapter = scope.ServiceProvider.GetRequiredService<IAccessSubjectProvider>();
                Assert.That(adapter, Is.TypeOf<Team21AccessSubjectProvider>());
                Assert.That(scope.ServiceProvider.GetRequiredService<IAccessSubjectProvider>(), Is.SameAs(adapter));
                first = await adapter.GetSubjectAsync(Context(false));
            }
            using (var scope = provider.CreateScope())
            {
                var second = await scope.ServiceProvider.GetRequiredService<IAccessSubjectProvider>()
                    .GetSubjectAsync(Context(false));
                Assert.That(ReferenceEquals(first, second), Is.EqualTo(lifetime == ServiceLifetime.Singleton));
            }
            Assert.That(disposals.Count, Is.EqualTo(lifetime == ServiceLifetime.Scoped ? 2 : 0));
        }
        finally { provider.Dispose(); }
        Assert.That(disposals.Count, Is.EqualTo(lifetime == ServiceLifetime.Scoped ? 2 : 1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExistingInstanceAndFactoryRegistrationsArePreserved(bool instanceRegistration)
    {
        var services = Services();
        var inner = new OriginalProvider(new DisposalCounter());
        if (instanceRegistration) services.AddSingleton<IAccessSubjectProvider>(inner);
        else services.AddScoped<IAccessSubjectProvider>(_ => inner);
        services.AddTeam21SessionAccess();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAccessSubjectProvider>().GetSubjectAsync(Context(false));
        Assert.That(result, Is.SameAs(inner.Subject));
    }

    [Test]
    public void MissingOriginalRegistrationIsRejected() =>
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTeam21SessionAccess());

    private static IServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentActor>(_ => new Actor(UserId));
        services.AddScoped<IAccountStatusReader>(_ => new Accounts());
        return services;
    }

    private static DefaultHttpContext Context(bool marked)
    {
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(marked ? [new Team21SessionRequiredMetadata()] : []), "test"));
        return http;
    }

    private sealed class Actor(Guid? userId) : ICurrentActor
    {
        public Guid? UserId => userId;
        public bool IsAuthenticated { get; set; } = userId is not null;
    }

    private sealed class Accounts : IAccountStatusReader
    {
        public AccountStatus Status { get; init; } = AccountStatus.Active;
        public bool Throw { get; init; }
        public int Calls { get; private set; }
        public Guid? LastId { get; private set; }
        public Task<AccountStatus> GetStatusAsync(Guid accountId, CancellationToken ct = default)
        {
            Calls++;
            LastId = accountId;
            return Throw ? throw new InvalidOperationException("Unavailable") : Task.FromResult(Status);
        }
    }

    private sealed class DisposalCounter { public int Count { get; set; } }

    private sealed class OriginalProvider(DisposalCounter counter) : IAccessSubjectProvider, IDisposable
    {
        public SubjectRef Subject { get; } = new(Guid.NewGuid().ToString("D"), AccountState.Active);
        public int Calls { get; private set; }
        public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult<SubjectRef?>(Subject);
        }
        public void Dispose() => counter.Count++;
    }
}
