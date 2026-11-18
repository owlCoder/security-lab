using System.Globalization;
using System.Net;
using System.Net.Sockets;

// IDN-22 TIM 09
namespace Platform.SessionSecurity.Domain;

/// <summary>
/// IDN-22 / AUD-03: masks a source IP address before it leaves the server. An IPv4 address keeps
/// its first three octets (<c>203.0.113.*</c>), an IPv6 address keeps its first four groups
/// (<c>2001:db8:85a3:8d3::*</c>). A missing or invalid address is fully masked, so the input is
/// never echoed back (SEC-05).
/// </summary>
public static class SourceAddressMasker
{
    public const string FullyMasked = "*";

    private const int KeptIpv4Octets = 3;
    private const int KeptIpv6Groups = 4;
    private const int BytesPerIpv6Group = 2;
    private const int BitsPerByte = 8;
    private const string HexFormat = "x";
    private const string Ipv4Separator = ".";
    private const string Ipv6Separator = ":";
    private const string Ipv4MaskSuffix = ".*";
    private const string Ipv6MaskSuffix = "::*";

    public static string Mask(string? sourceAddress)
    {
        if (string.IsNullOrWhiteSpace(sourceAddress)
            || !IPAddress.TryParse(sourceAddress, out var address))
        {
            return FullyMasked;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // TryParse also accepts shortened or octal/hex forms such as "1" or "0xCB.0.113.45";
            // only the canonical dotted form is trusted.
            return address.ToString() == sourceAddress ? MaskIpv4(address) : FullyMasked;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return FullyMasked;
        }

        return address.IsIPv4MappedToIPv6 ? MaskIpv4(address.MapToIPv4()) : MaskIpv6(address);
    }

    private static string MaskIpv4(IPAddress address)
    {
        var keptOctets = address.GetAddressBytes()
            .Take(KeptIpv4Octets)
            .Select(octet => octet.ToString(CultureInfo.InvariantCulture));

        return string.Join(Ipv4Separator, keptOctets) + Ipv4MaskSuffix;
    }

    private static string MaskIpv6(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        var keptGroups = Enumerable.Range(0, KeptIpv6Groups)
            .Select(group => ((bytes[BytesPerIpv6Group * group] << BitsPerByte)
                    | bytes[(BytesPerIpv6Group * group) + 1])
                .ToString(HexFormat, CultureInfo.InvariantCulture));

        return string.Join(Ipv6Separator, keptGroups) + Ipv6MaskSuffix;
    }
}
