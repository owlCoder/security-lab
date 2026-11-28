using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Identity.Testing;
using Platform.Identity.Tests.Helpers;

// IDN-22 TIM 09
namespace Platform.Identity.Tests.Integration.Login;

/// <summary>
/// IDN-22 / AUD-01: a login over HTTP stores the client address as sourceAddress in the real audit table, and
/// the catalog accepts it. A zoned or IPv4-mapped client address is normalized, so the login is not refused
/// for an audit value it cannot store. The response never contains the address.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("IDN-22")]
public sealed class LoginSourceAddressIntegrationTests : IntegrationTestBase
{
    private const string Email = "user-a@example.com";
    private const string Password = "synthetic-password-2026";
    private const string WrongPassword = "wrong-password-2026";

    [TestCase("203.0.113.7", "203.0.113.7")]
    [TestCase("2001:db8::1", "2001:db8::1")]
    [TestCase("::ffff:198.51.100.4", "198.51.100.4")]
    [TestCase("fe80::1%2", "fe80::1")]
    public async Task Login_ValidCredentials_ShouldRecordNormalizedSourceAddress(
        string clientAddress,
        string expected)
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, Email, Password);
        using var host = AuditedHostFrom(clientAddress);
        using var client = host.CreateCsrfClient();

        // Act
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = Password });

        // Assert
        var row = await LoginRowAsync(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(row.Outcome, Is.EqualTo("Success"));
            Assert.That(row.ActorId, Is.EqualTo(user.Id));
            Assert.That(SourceAddressOf(row), Is.EqualTo(expected));
            Assert.That(body, Does.Not.Contain(expected));
        });
    }

    [Test]
    public async Task Login_WrongPassword_ShouldRecordSourceAddressForAnonymousActor()
    {
        // Arrange
        await UserTestData.AddAsync(Factory, Email, Password);
        using var host = AuditedHostFrom("203.0.113.9");
        using var client = host.CreateCsrfClient();

        // Act
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = Email, password = WrongPassword });

        // Assert
        var row = await LoginRowAsync(response);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(row.Outcome, Is.EqualTo("Failure"));
            Assert.That(row.ActorId, Is.Null);
            Assert.That(SourceAddressOf(row), Is.EqualTo("203.0.113.9"));
            Assert.That(row.Raw, Does.Not.Contain(WrongPassword));
        });
    }

    [Test]
    public async Task Login_UnknownAccount_ShouldRecordSourceAddressWithoutResource()
    {
        // Arrange
        using var host = AuditedHostFrom("198.51.100.20");
        using var client = host.CreateCsrfClient();

        // Act
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = "user-b@example.com", password = Password });

        // Assert
        var row = await LoginRowAsync(response);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(ResourceIdOf(row), Is.Null);
            Assert.That(SourceAddressOf(row), Is.EqualTo("198.51.100.20"));
            Assert.That(row.Raw, Does.Not.Contain("user-b@example.com"));
        });
    }

    // The shared factory stores no audit rows, so this host restores the real writer; ConnectionStrings:Audit is
    // the test database.
    private static WebApplicationFactory<Program> AuditedHostFrom(string clientAddress) =>
        HostWithClientAddress(IPAddress.Parse(clientAddress)).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITrustedAuditWriter>();
                services.AddScoped<ITrustedAuditWriter>(provider => provider.GetRequiredService<AuditWriter>());
            }));

    private static async Task<AuditTestData.Row> LoginRowAsync(HttpResponseMessage response)
    {
        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        return (await AuditTestData.RowsAsync(correlationId, AuditEventTypes.AuthLogin)).Single();
    }

    private static string? SourceAddressOf(AuditTestData.Row row)
    {
        using var json = JsonDocument.Parse(row.Raw);
        return json.RootElement.GetProperty("details").TryGetProperty("sourceAddress", out var value)
            ? value.GetString()
            : null;
    }

    private static string? ResourceIdOf(AuditTestData.Row row)
    {
        using var json = JsonDocument.Parse(row.Raw);
        return json.RootElement.GetProperty("resource_id").GetString();
    }
}
