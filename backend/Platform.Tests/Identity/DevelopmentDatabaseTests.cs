using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Api.Services.Identity;
using Platform.Identity;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("ADM-02")]
public class DevelopmentDatabaseTests
{
    private const string Password = "test-only-password";
    private const string UnreachableDatabase = $"Host=127.0.0.1;Port=1;Timeout=1;Database=model_only;Username=test;Password={Password}";

    [TestCase("Production")]
    [TestCase("Staging")]
    public async Task Migrations_Throw_OutsideDevelopment_BeforeTouchingTheDatabase(string environmentName)
    {
        await using var app = BuildApp(environmentName, UnreachableDatabase);

        Assert.ThrowsAsync<InvalidOperationException>(() => app.ApplyIdentityMigrationsAsync());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task Migrations_AreSkipped_WithoutConnectionString(string? connectionString)
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = BuildApp(Environments.Development, connectionString, logs);

        Assert.That(await app.ApplyIdentityMigrationsAsync(), Is.False);
        Assert.That(logs.Messages, Has.Some.Contains("connection string is not set"));
    }

    [Test]
    public async Task UnreachableDatabase_ReportsFalse_InsteadOfStoppingTheApi()
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = BuildApp(Environments.Development, UnreachableDatabase, logs);

        Assert.That(await app.ApplyIdentityMigrationsAsync(), Is.False);
        Assert.That(logs.Messages, Has.Some.Contains("migrations were not applied"));
    }

    [Test]
    public async Task UnreachableDatabase_DoesNotLogTheConnectionPassword()
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = BuildApp(Environments.Development, UnreachableDatabase, logs);

        await app.ApplyIdentityMigrationsAsync();

        Assert.That(logs.Messages, Has.None.Contains(Password));
    }

    private static WebApplication BuildApp(string environmentName, string? connectionString, ILoggerProvider? logs = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{IdentityModule.ConnectionStringName}"] = connectionString
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddIdentityModule(builder.Configuration);
        if (logs is not null) builder.Logging.AddProvider(logs);

        return builder.Build();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IEnumerable<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _messages.Enqueue($"{formatter(state, exception)} {exception}");

        public void Dispose() { }
    }
}
