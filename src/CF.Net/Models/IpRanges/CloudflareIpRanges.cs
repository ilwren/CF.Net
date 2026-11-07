using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.IpRanges;

/// <summary>
/// The IP ranges Cloudflare's edge answers from, as <c>GET /ips</c> reports them.
/// </summary>
/// <remarks>
/// <para>
/// This is the list behind <c>https://www.cloudflare.com/ips/</c>, and the reason most callers want it is a
/// firewall: an origin that only accepts traffic from Cloudflare needs these ranges and nothing else, and a
/// service that trusts <c>CF-Connecting-IP</c> needs to know the request really came through the edge. The
/// endpoint is public - Cloudflare publishes no permission group for it - so a token that can do nothing else
/// can still read it.
/// </para>
/// <para>
/// <see cref="Etag"/> is a digest of the data rather than an HTTP header echoed back, so a caller that caches
/// the ranges can compare it instead of comparing every range. Cloudflare changes these ranges rarely and
/// announces it well ahead of time, which is what makes caching them safe at all.
/// </para>
/// <para>
/// <see cref="JdCloudCidrs"/> is only answered when the call asks for it with <c>?networks=jdcloud</c> - the
/// China network's ranges, which Cloudflare operates jointly with JD Cloud and lists separately from its own.
/// </para>
/// </remarks>
public sealed class CloudflareIpRanges
{
    /// <summary>A digest of the IP data, useful for telling whether the ranges have changed.</summary>
    [JsonPropertyName("etag")]
    public string? Etag { get; set; }

    /// <summary>Cloudflare's IPv4 ranges, in CIDR notation.</summary>
    [JsonPropertyName("ipv4_cidrs")]
    public List<string>? Ipv4Cidrs { get; set; }

    /// <summary>Cloudflare's IPv6 ranges, in CIDR notation.</summary>
    [JsonPropertyName("ipv6_cidrs")]
    public List<string>? Ipv6Cidrs { get; set; }

    /// <summary>
    /// The JD Cloud ranges of the China network, IPv4 and IPv6 together. Only populated when the call asked
    /// for <c>?networks=jdcloud</c>, so it stays null on an ordinary read.
    /// </summary>
    [JsonPropertyName("jdcloud_cidrs")]
    public List<string>? JdCloudCidrs { get; set; }

    /// <summary>
    /// Anything else Cloudflare sent. Older revisions of this endpoint also answered the China network's own
    /// ranges beside these, so a field arriving here is a field worth modelling next rather than a surprise.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// Every range in the answer, IPv4 then IPv6 then JD Cloud. This is what a firewall rule wants, and what
    /// <see cref="Contains(string?)" /> walks.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<string> AllCidrs
    {
        get
        {
            foreach (var cidr in Ipv4Cidrs ?? new List<string>())
            {
                yield return cidr;
            }

            foreach (var cidr in Ipv6Cidrs ?? new List<string>())
            {
                yield return cidr;
            }

            foreach (var cidr in JdCloudCidrs ?? new List<string>())
            {
                yield return cidr;
            }
        }
    }

    /// <summary>True when this answer carries the JD Cloud ranges, which only a <c>networks=jdcloud</c> call does.</summary>
    [JsonIgnore]
    public bool IncludesJdCloud => JdCloudCidrs is { Count: > 0 };

    /// <summary>
    /// Asks whether an address sits inside any range this answer listed - the question an origin allowlist and
    /// a <c>CF-Connecting-IP</c> check both come down to.
    /// </summary>
    /// <param name="address">An IPv4 or IPv6 address; a range of its own is not accepted.</param>
    /// <returns>True when the address falls inside one of the listed ranges.</returns>
    /// <remarks>
    /// Anything that does not parse is answered <c>false</c> rather than refused: this is a filter, and a
    /// malformed address is simply not a Cloudflare one. An address is only ever compared with ranges of its
    /// own family, and <see cref="IPAddress.Parse(string)" /> is what decides the family - so an IPv4-mapped
    /// IPv6 address such as <c>::ffff:199.27.128.5</c> belongs to the IPv6 family and is matched against the
    /// IPv6 ranges, not the IPv4 ones. Callers that hold one and want the other should use
    /// <see cref="IPAddress.MapToIPv4" /> first.
    /// </remarks>
    public bool Contains(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !IPAddress.TryParse(address.Trim(), out var candidate))
        {
            return false;
        }

        foreach (var cidr in AllCidrs)
        {
            if (Covers(cidr, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Counts the ranges and says which kinds of range this answer carried.</summary>
    /// <returns>A one-line description of the answer.</returns>
    public override string ToString()
    {
        var jdcloud = IncludesJdCloud ? $" and {JdCloudCidrs!.Count} JD Cloud" : string.Empty;
        return $"{Ipv4Cidrs?.Count ?? 0} IPv4 and {Ipv6Cidrs?.Count ?? 0} IPv6 range(s){jdcloud}";
    }

    private static bool Covers(string? cidr, IPAddress address)
    {
        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        var parts = cidr.Split('/');
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var network)
            || !int.TryParse(parts[1], out var prefixLength))
        {
            return false;
        }

        // A range never covers an address of the other family, and comparing bytes across families would
        // compare a 4-byte array with a 16-byte one.
        if (network.AddressFamily != address.AddressFamily)
        {
            return false;
        }

        var networkBytes = network.GetAddressBytes();
        var addressBytes = address.GetAddressBytes();
        if (prefixLength < 0 || prefixLength > (networkBytes.Length * 8))
        {
            return false;
        }

        var wholeBytes = prefixLength / 8;
        for (var index = 0; index < wholeBytes; index++)
        {
            if (networkBytes[index] != addressBytes[index])
            {
                return false;
            }
        }

        var leftoverBits = prefixLength % 8;
        if (leftoverBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - leftoverBits));
        return (networkBytes[wholeBytes] & mask) == (addressBytes[wholeBytes] & mask);
    }
}

/// <summary>What <c>GET /ips</c> should answer with.</summary>
public sealed class IpRangesListParameters : ICloudflareQueryParameters
{
    /// <summary>
    /// Which network's ranges to add. Cloudflare publishes one value, <c>jdcloud</c>, which brings the China
    /// network's ranges back in <c>jdcloud_cidrs</c>; leave null for Cloudflare's own ranges only.
    /// </summary>
    public string? Networks { get; set; }

    /// <summary>Asks for the JD Cloud ranges as well as Cloudflare's own.</summary>
    /// <returns>Parameters carrying <c>networks=jdcloud</c>.</returns>
    public static IpRangesListParameters IncludingJdCloud() => new() { Networks = JdCloud };

    /// <summary>The one value Cloudflare publishes for <see cref="Networks" />.</summary>
    public const string JdCloud = "jdcloud";

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query) => query.AddIfNotEmpty("networks", Networks);
}
