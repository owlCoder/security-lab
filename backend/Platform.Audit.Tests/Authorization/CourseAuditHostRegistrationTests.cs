using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Audit.Application.Authorization;

namespace Platform.Audit.Tests.Authorization;

[TestFixture]
public sealed class CourseAuditHostRegistrationTests
{
    [Test]
    public async Task RealApiHost_RegistersAuditPolicyDuringComposition()
    {
        using var factory = new StrictHostFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/health");
        var registry = factory.Services.GetRequiredService<IAccessPolicyRegistry>();
        var found = registry.TryGet(CourseAuditReadPolicy.ActionId, out var policy);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(found, Is.True);
            Assert.That(policy, Is.TypeOf<CourseAuditReadPolicy>());
        });
    }
}
