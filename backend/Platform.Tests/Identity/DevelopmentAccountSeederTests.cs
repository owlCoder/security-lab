using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.Api.Services.Identity;
using Platform.Identity;
using Platform.Identity.Data;
using Platform.Identity.Domain;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("OPS-01")]
public class DevelopmentAccountSeederTests
{
    [Test]
    public void All_ContainsStudentTeacherAdministratorAndBlockedAccount()
    {
        Assert.That(DevelopmentAccountSeeder.All, Has.Count.EqualTo(4));
        Assert.That(DevelopmentAccountSeeder.All.Count(account => account.Status == AccountStatus.Blocked), Is.EqualTo(1));
        Assert.That(
            DevelopmentAccountSeeder.All
                .Where(account => account.Status == AccountStatus.Active)
                .Select(account => account.Role),
            Is.EquivalentTo(new[] { DevelopmentRole.Student, DevelopmentRole.Teacher, DevelopmentRole.Administrator }));
    }

    [Test]
    public void All_HaveUniqueIdsAndEmails()
    {
        Assert.That(
            DevelopmentAccountSeeder.All.Select(account => account.Id).Distinct().Count(),
            Is.EqualTo(DevelopmentAccountSeeder.All.Count));
        Assert.That(
            DevelopmentAccountSeeder.All.Select(account => account.Email).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(DevelopmentAccountSeeder.All.Count));
    }

    [Test]
    public void All_UseTheReservedExampleDomain()
    {
        Assert.That(
            DevelopmentAccountSeeder.All.Select(account => account.Email),
            Has.All.Matches<string>(email => email.EndsWith("@example.com")));
    }

    [Test]
    public void Administrator_UsesTheIdFromStubAdminIds()
    {
        var administrator = DevelopmentAccountSeeder.All.Single(account => account.Role == DevelopmentRole.Administrator);
        Assert.That(administrator.Id, Is.EqualTo(DevelopmentAccountSeeder.AdminId));

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        if (!File.Exists(settingsPath))
            Assert.Ignore("appsettings.Development.json is not available in the test output directory.");

        using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var adminIds = settings.RootElement.GetProperty("Stub").GetProperty("AdminIds").GetString();
        Assert.That(adminIds, Does.Contain(DevelopmentAccountSeeder.AdminId.ToString()));
    }

    [TestCase("Production")]
    [TestCase("Staging")]
    public void RequireDevelopment_Throws_OutsideDevelopment(string environmentName)
    {
        Assert.Throws<InvalidOperationException>(
            () => DevelopmentAccountSeeder.RequireDevelopment(EnvironmentNamed(environmentName)));
    }

    [Test]
    public void RequireDevelopment_Allows_Development()
    {
        Assert.DoesNotThrow(
            () => DevelopmentAccountSeeder.RequireDevelopment(EnvironmentNamed(Environments.Development)));
    }

    [TestCase("Production")]
    [TestCase("Staging")]
    public void SeedAsync_Throws_OutsideDevelopment_BeforeTouchingTheDatabase(string environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{IdentityModule.ConnectionStringName}"] = "Host=localhost;Database=model_only"
            })
            .Build();
        using var provider = new ServiceCollection()
            .AddIdentityModule(configuration)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();

        Assert.ThrowsAsync<InvalidOperationException>(
            () => DevelopmentAccountSeeder.SeedAsync(context, EnvironmentNamed(environmentName)));
    }

    private static IHostEnvironment EnvironmentNamed(string name) =>
        WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = name }).Environment;
}
