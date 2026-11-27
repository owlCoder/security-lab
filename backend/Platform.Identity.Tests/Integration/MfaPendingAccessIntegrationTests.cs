using System.Net;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

[Category("US-19")]
public class MfaPendingAccessIntegrationTests : IntegrationTestBase
{
    [TestCase("/api/auth/me")]
    [TestCase("/api/laboratories/search")]
    [TestCase("/api/laboratories")]
    [TestCase("/api/revisions/mine")]
    [TestCase("/api/authoring/templates")]
    public async Task MfaPendingSession_IsRefusedOnProtectedEndpoint(string url)
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-pending@example.com", "Password123!");
        var issued = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={issued.RawToken}");

        var response = await Client.GetAsync(url);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
