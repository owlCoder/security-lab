using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Security.IdentityAccess;

namespace Platform.Access.Tests;

[TestFixture]
public sealed class SystemRoleSubjectProviderTests
{
    [TestCase(true, SystemRole.Administrator)]
    [TestCase(false, SystemRole.None)]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_AdminDirectoryResult_AdminOrNone(bool isAdmin, SystemRole expected)
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new FixedSubjectProvider(new SubjectRef(id.ToString("D"), AccountState.Active));
        var provider = Provider(inner, new FixedAdminDirectory(isAdmin));

        var subject = provider.GetSubjectAsync(new DefaultHttpContext()).AsTask().GetAwaiter().GetResult();

        Assert.That(subject?.SystemRole, Is.EqualTo(expected));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_InvalidSubjectId_AdminRoleRemainsNone()
    {
        var inner = new FixedSubjectProvider(new SubjectRef("not-a-guid", AccountState.Active));
        var subject = Provider(inner, new FixedAdminDirectory(true))
            .GetSubjectAsync(new DefaultHttpContext()).AsTask().GetAwaiter().GetResult();

        Assert.That(subject?.SystemRole, Is.EqualTo(SystemRole.None));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_RequestHeaderRole_IsIgnoredAndStatePreserved()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var inner = new FixedSubjectProvider(new SubjectRef(id.ToString("D"), AccountState.Active,
            SystemRole.Administrator));
        var http = new DefaultHttpContext();
        http.Request.Headers["SystemRole"] = "Administrator";

        var subject = Provider(inner, new FixedAdminDirectory(false))
            .GetSubjectAsync(http).AsTask().GetAwaiter().GetResult();

        Assert.That(subject?.State, Is.EqualTo(AccountState.Active));
        Assert.That(subject?.SystemRole, Is.EqualTo(SystemRole.None));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_NullInnerSubject_RemainsNull()
    {
        var subject = Provider(new FixedSubjectProvider(null), new FixedAdminDirectory(true))
            .GetSubjectAsync(new DefaultHttpContext()).AsTask().GetAwaiter().GetResult();

        Assert.That(subject, Is.Null);
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_AdminDirectoryException_FailsClosedAndLogsOnlyExceptionType()
    {
        var logger = new RecordingLogger<SystemRoleSubjectProvider>();
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var provider = new SystemRoleSubjectProvider(
            new FixedSubjectProvider(new SubjectRef(id.ToString("D"), AccountState.Active)),
            new ThrowingAdminDirectory(),
            logger);

        var subject = provider.GetSubjectAsync(new DefaultHttpContext()).AsTask().GetAwaiter().GetResult();

        Assert.That(subject?.SystemRole, Is.EqualTo(SystemRole.None));
        Assert.That(logger.Messages, Has.Count.EqualTo(1));
        Assert.That(logger.Messages[0], Does.Contain(nameof(InvalidOperationException)));
        Assert.That(logger.Messages[0], Does.Not.Contain(id.ToString("D")));
        Assert.That(logger.Messages[0], Does.Not.Contain("sensitive exception details"));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_RegistrationWithoutUnkeyedProvider_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddSystemRoleSubjectProvider());
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_KeyedProviderOnly_IsIgnoredByRegistration()
    {
        var services = new ServiceCollection();
        services.AddKeyedScoped<IAccessSubjectProvider, KeyedSubjectProvider>("keyed");

        Assert.Throws<InvalidOperationException>(() =>
            services.AddSystemRoleSubjectProvider());
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_RegistrationDecoratesFinalProviderSubject()
    {
        var adminId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var innerId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAdminDirectory>(new FixedAdminDirectory(true));
        services.AddScoped<IAccessSubjectProvider>(_ =>
            new OutermostSubjectProvider(
                new FixedSubjectProvider(new SubjectRef(innerId.ToString("D"), AccountState.Active)),
                new SubjectRef(adminId.ToString("D"), AccountState.Blocked)));
        services.AddSystemRoleSubjectProvider();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var subject = scope.ServiceProvider.GetRequiredService<IAccessSubjectProvider>()
            .GetSubjectAsync(new DefaultHttpContext()).AsTask().GetAwaiter().GetResult();

        Assert.That(subject?.Id, Is.EqualTo(adminId.ToString("D")));
        Assert.That(subject?.State, Is.EqualTo(AccountState.Blocked));
        Assert.That(subject?.SystemRole, Is.EqualTo(SystemRole.Administrator));
    }

    private static SystemRoleSubjectProvider Provider(
        IAccessSubjectProvider inner,
        IAdminDirectory admins) =>
        new(inner, admins, NullLogger<SystemRoleSubjectProvider>.Instance);

    private sealed class FixedSubjectProvider(SubjectRef? subject) : IAccessSubjectProvider
    {
        public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(subject);
    }

    private sealed class OutermostSubjectProvider(
        IAccessSubjectProvider inner,
        SubjectRef output) : IAccessSubjectProvider
    {
        public async ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext,
            CancellationToken cancellationToken = default)
        {
            await inner.GetSubjectAsync(httpContext, cancellationToken);
            return output;
        }
    }

    private sealed class KeyedSubjectProvider : IAccessSubjectProvider
    {
        public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SubjectRef?>(null);
    }

    private sealed class FixedAdminDirectory(bool isAdmin) : IAdminDirectory
    {
        public bool IsAdministrator(Guid userId) => isAdmin;
    }

    private sealed class ThrowingAdminDirectory : IAdminDirectory
    {
        public bool IsAdministrator(Guid userId) =>
            throw new InvalidOperationException("sensitive exception details");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
