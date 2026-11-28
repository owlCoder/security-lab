using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Platform.Api.Development;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Development;

/// <summary>
/// US-02: identity migrations on startup, run on the shared test host. None of these cases reaches a database
/// (wrong environment, no connection string, unreachable server), so the class does not use the Testcontainers
/// database and does not inherit <c>IntegrationTestBase</c>.
/// </summary>
[TestFixture]
[Category("US-02")]
public sealed class DevelopmentDatabaseTests
{
    private const string Password = "test-only-password";
    private const string UnreachableDatabase = $"Host=127.0.0.1;Port=1;Timeout=1;Database=model_only;Username=test;Password={Password}";

    private CustomWebApplicationFactory<Program> _factory = null!;
    private Mock<ILogger> _logger = null!;

    [OneTimeSetUp]
    public void CreateFactory() => _factory = new CustomWebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void DisposeFactory() => _factory.Dispose();

    [SetUp]
    public void CreateLogger()
    {
        _logger = new Mock<ILogger>();
        _logger.Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
    }

    [TestCase("Production")]
    [TestCase("Staging")]
    public async Task ApplyIdentityMigrations_OutsideDevelopment_ShouldThrowBeforeTouchingTheDatabase(string environmentName)
    {
        // Arrange
        using var host = Host(environmentName, UnreachableDatabase);
        var services = StartedServices(host);

        // Act & Assert
        await Assert.ThatAsync(() => services.ApplyIdentityMigrationsAsync(), Throws.InvalidOperationException);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task ApplyIdentityMigrations_WithoutConnectionString_ShouldReturnFalse_AndLogThatTheyAreSkipped(string? connectionString)
    {
        // Arrange
        using var host = Host(Environments.Development, connectionString);
        var services = StartedServices(host);

        // Act
        var applied = await services.ApplyIdentityMigrationsAsync();

        // Assert
        Assert.That(applied, Is.False);
        VerifyLogged(LogLevel.Information, "connection string is not set", Times.Once());
    }

    [Test]
    public async Task ApplyIdentityMigrations_UnreachableDatabase_ShouldReturnFalse_AndLogAWarning()
    {
        // Arrange
        using var host = Host(Environments.Development, UnreachableDatabase);
        var services = StartedServices(host);

        // Act
        var applied = await services.ApplyIdentityMigrationsAsync();

        // Assert
        Assert.That(applied, Is.False, "the API keeps starting instead of stopping");
        VerifyLogged(LogLevel.Warning, "migrations were not applied", Times.Once());
    }

    [Test]
    public async Task ApplyIdentityMigrations_UnreachableDatabase_ShouldNotLogTheConnectionPassword()
    {
        // Arrange
        using var host = Host(Environments.Development, UnreachableDatabase);
        var services = StartedServices(host);

        // Act
        await services.ApplyIdentityMigrationsAsync();

        // Assert
        VerifyLogged(level: null, Password, Times.Never());
        _logger.Verify(logger => logger.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.Is<Exception?>(exception => exception != null && exception.ToString().Contains(Password)),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never());
    }

    // The shared host in another environment, with another identity connection string, logging to _logger.
    private WebApplicationFactory<Program> Host(string environmentName, string? connectionString)
    {
        var logs = new Mock<ILoggerProvider>();
        logs.Setup(provider => provider.CreateLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
        logs.Setup(provider => provider.CreateLogger(nameof(DevelopmentDatabase))).Returns(_logger.Object);

        return _factory.WithWebHostBuilder(builder => builder
            .UseEnvironment(environmentName)
            // Not UseSetting: in Development, Program.cs adds the local .env and user secrets after it, so a
            // developer's own connection string would win. A source added here comes last and wins, even as null.
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { [$"ConnectionStrings:{IdentityModule.ConnectionStringName}"] = connectionString }))
            .ConfigureLogging(logging => logging.AddProvider(logs.Object))
            // The shared factory points the identity context at the Testcontainers database. These cases need the
            // server the setting names, so the context is configured once more with that connection string.
            // EF Core applies every AddDbContext configuration of a context in order, so this last one wins. Do not
            // turn this into a replace-only pattern without checking it: the test could then quietly connect to the
            // Testcontainers database and stop checking an unreachable server.
            .ConfigureTestServices(services =>
                services.AddDbContext<PlatformIdentityDbContext>(options => options.UseNpgsql(connectionString))));
    }

    // Starts the host and forgets what it logged on startup (Program.cs already runs the migrations once in
    // Development), so the assertions see only the call under test.
    private IServiceProvider StartedServices(WebApplicationFactory<Program> host)
    {
        var services = host.Services;
        _logger.Invocations.Clear();
        return services;
    }

    // A null level matches every level.
    private void VerifyLogged(LogLevel? level, string text, Times times) =>
        _logger.Verify(logger => logger.Log(
            It.Is<LogLevel>(logged => level == null || logged == level),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state != null && state.ToString()!.Contains(text)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);
}
