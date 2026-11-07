using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;

namespace Laboratories.Tests;

public sealed class ReviewAuthorizationTests
{
    [TestCase(false, HttpStatusCode.Unauthorized)]
    [TestCase(true, HttpStatusCode.Forbidden)]
    public async Task Approve_WithoutRegisteredPolicy_DeniesBeforeDatabase(bool authenticated, HttpStatusCode expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequestSubjectResolver>();
                services.RemoveAll<IAccessSubjectProvider>();
                var resolver = new TestResolver(authenticated);
                services.AddSingleton<IRequestSubjectResolver>(resolver);
                services.AddSingleton<IAccessSubjectProvider>(resolver);
            });
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/revisions/{Guid.NewGuid()}/approve")
        {
            Content = JsonContent.Create(new
            {
                rowVersion = "2",
                contentHash = new string('a', 64)
            })
        };
        request.Headers.Add("Idempotency-Key", "test-request");
        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    private sealed class TestResolver(bool authenticated) : IRequestSubjectResolver, IAccessSubjectProvider
    {
        public SubjectRef? Resolve(Microsoft.AspNetCore.Http.HttpContext context) =>
            authenticated ? new SubjectRef("11111111-1111-1111-1111-111111111111", AccountState.Active) : null;

        public ValueTask<SubjectRef?> GetSubjectAsync(Microsoft.AspNetCore.Http.HttpContext context,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Resolve(context));
    }
}
