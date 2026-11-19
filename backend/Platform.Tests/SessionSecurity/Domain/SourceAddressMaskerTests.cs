using Platform.SessionSecurity.Domain;

// IDN-22 TIM 09
namespace Platform.Tests.SessionSecurity.Domain;

[TestFixture]
public sealed class SourceAddressMaskerTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-an-address")]
    [TestCase("1")]
    [TestCase("203.0.113")]
    [TestCase("203.0.113.045")]
    [TestCase("0xCB.0.113.45")]
    [TestCase("999.0.113.45")]
    [TestCase("203.0.113.45; DROP TABLE login_events")]
    [TestCase("<script>alert(1)</script>")]
    public void Mask_WhenAddressIsMissingOrInvalid_ReturnsFullyMasked(string? sourceAddress)
    {
        var masked = SourceAddressMasker.Mask(sourceAddress);

        Assert.That(masked, Is.EqualTo(SourceAddressMasker.FullyMasked));
    }

    [TestCase("203.0.113.45", "203.0.113.*")]
    [TestCase("198.51.100.7", "198.51.100.*")]
    [TestCase("192.0.2.0", "192.0.2.*")]
    public void Mask_WhenAddressIsIpv4_HidesLastOctet(string sourceAddress, string expected)
    {
        var masked = SourceAddressMasker.Mask(sourceAddress);

        Assert.That(masked, Is.EqualTo(expected));
    }

    [TestCase("2001:db8:85a3:8d3:1319:8a2e:370:7348", "2001:db8:85a3:8d3::*")]
    [TestCase("2001:0DB8:85A3:08D3:1319:8A2E:0370:7348", "2001:db8:85a3:8d3::*")]
    [TestCase("2001:db8::1", "2001:db8:0:0::*")]
    public void Mask_WhenAddressIsIpv6_KeepsOnlyFirstFourGroups(string sourceAddress, string expected)
    {
        var masked = SourceAddressMasker.Mask(sourceAddress);

        Assert.That(masked, Is.EqualTo(expected));
    }

    [Test]
    public void Mask_WhenAddressIsIpv4MappedIpv6_MasksAsIpv4()
    {
        var masked = SourceAddressMasker.Mask("::ffff:203.0.113.45");

        Assert.That(masked, Is.EqualTo("203.0.113.*"));
    }

    [TestCase("203.0.113.45", "45")]
    [TestCase("2001:db8:85a3:8d3:1319:8a2e:370:7348", "1319:8a2e:370:7348")]
    public void Mask_WhenAddressIsValid_DoesNotContainHiddenPart(string sourceAddress, string hiddenPart)
    {
        var masked = SourceAddressMasker.Mask(sourceAddress);

        Assert.That(masked, Does.Not.EndWith(hiddenPart));
    }
}
