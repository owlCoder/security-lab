using Moq;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Identity.Tests.Services.Accounts;

[TestFixture]
[Category("IDN-05")]
public sealed class AccountAdministrationDecisionTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, false)]
    public void CanUnlockAccount_AuthenticationAndDirectoryMembership_ReturnsExpectedDecision(
        bool authenticated, bool administrator, bool expected)
    {
        // Arrange
        var admins = new Mock<IAdminDirectory>(MockBehavior.Strict);
        admins.Setup(x => x.IsAdministrator(UserId)).Returns(administrator);
        var policy = new AdminOnlyAccountAdministrationDecision(admins.Object);
        var caller = new CallerIdentity(authenticated, UserId, AuthenticationLevel.None, Guid.Empty);

        // Act
        var allowed = policy.CanUnlockAccount(caller);

        // Assert
        Assert.That(allowed, Is.EqualTo(expected));
        admins.Verify(x => x.IsAdministrator(UserId), authenticated ? Times.Once() : Times.Never());
        admins.VerifyNoOtherCalls();
    }

    [Test]
    public void CanUnlockAccount_CallerWithoutUserId_ReturnsDenied()
    {
        // Arrange
        var admins = new Mock<IAdminDirectory>(MockBehavior.Strict);
        admins.Setup(x => x.IsAdministrator(Guid.Empty)).Returns(false);
        var policy = new AdminOnlyAccountAdministrationDecision(admins.Object);
        var caller = new CallerIdentity(true, null, AuthenticationLevel.None, Guid.Empty);

        // Act
        var allowed = policy.CanUnlockAccount(caller);

        // Assert
        Assert.That(allowed, Is.False);
        admins.Verify(x => x.IsAdministrator(Guid.Empty), Times.Once());
        admins.VerifyNoOtherCalls();
    }
}
