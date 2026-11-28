using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Application.Options;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Endpoints.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Services;

namespace Platform.Access.Tests;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public sealed class AccountEndpointAccessTests
{
    private sealed class TestEmailConfirmationService : IEmailConfirmationService
    {
        public Task<EmailConfirmationGrant?> IssueAsync(
            string email,
            CancellationToken ct = default) =>
            Task.FromResult<EmailConfirmationGrant?>(new EmailConfirmationGrant(email, "test-token"));

        public Task<bool> ConfirmAsync(string token, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(
            string recipient,
            string subject,
            string body,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingAccountRegistration : IAccountRegistration
    {
        public List<RegisterAccountCommand> Commands { get; } = [];

        public ValueTask<RegisterAccountResult> RegisterAsync(
            RegisterAccountCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return ValueTask.FromResult(RegisterAccountResult.Created(Guid.NewGuid()));
        }
    }

    [Test]
    public async Task Admin_can_create_account_through_central_access_policy()
    {
        var administratorId = Guid.NewGuid();
        var registration = new RecordingAccountRegistration();
        var app = await CreateApp(
            administratorId,
            administratorId,
            registration);
        await using (app)
        using (var client = app.GetTestClient())
        {
            var response = await client.PostAsJsonAsync(
                "/api/admin/accounts",
                new { Email = "new-user@example.test", Password = "Strong-Password!2026" });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(registration.Commands, Has.Count.EqualTo(1));
            Assert.That(registration.Commands.Single().Email, Is.EqualTo("new-user@example.test"));
        }
    }

    [Test]
    public async Task Authenticated_non_admin_cannot_create_account()
    {
        var administratorId = Guid.NewGuid();
        var registration = new RecordingAccountRegistration();
        var app = await CreateApp(
            administratorId,
            Guid.NewGuid(),
            registration);
        await using (app)
        using (var client = app.GetTestClient())
        {
            var response = await client.PostAsJsonAsync(
                "/api/admin/accounts",
                new { Email = "new-user@example.test", Password = "Strong-Password!2026" });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(registration.Commands, Is.Empty);
        }
    }

    private static async Task<WebApplication> CreateApp(
        Guid administratorId,
        Guid subjectId,
        RecordingAccountRegistration registration)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:AdminAccountIds:0"] = administratorId.ToString("D"),
            ["Identity:DevSubjectId"] = subjectId.ToString("D")
        });
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IRequestSubjectResolver, DevelopmentSubjectResolver>();
        builder.Services.AddScoped<IAccessSubjectProvider, ResolverAccessSubjectProvider>();
        builder.Services.AddSingleton<IAccountRegistration>(registration);
        builder.Services.AddSingleton<IEmailConfirmationService, TestEmailConfirmationService>();
        builder.Services.AddSingleton<IEmailSender, NoopEmailSender>();
        builder.Services.AddSingleton<IAccessAuditWriter, NoopAuditWriter>();
        builder.Services.AddSingleton(new AccessConfigurationOptions { RequirePolicyCoverage = false });
        builder.Services
            .AddAccessAction<CreateAccountPolicy>(
                new ActionDefinition(
                    CreateAccountPolicy.ActionId,
                    ActionScope.Authenticated,
                    null,
                    false))
            .AddAccessControl();

        var app = builder.Build();
        app.UseRouting();
        app.UseAccessControl();
        app.MapAccountEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class NoopAuditWriter : IAccessAuditWriter
    {
        public ValueTask WriteAsync(
            AccessRequest request,
            AccessDecision decision,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
