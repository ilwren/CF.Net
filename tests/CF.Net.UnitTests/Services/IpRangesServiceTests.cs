using System.Text.Json;
using CFNet.Models.IpRanges;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// <c>GET /ips</c>: one endpoint, no account, no zone and no permission group. Worth pinning because the path
/// is at the API root - a caller who assumes every endpoint is account-scoped would look for it in the wrong
/// place - and because the answer is a list of CIDR ranges, which is only useful if the library can also say
/// whether a given address falls inside one.
/// </summary>
public class IpRangesServiceTests
{
    /// <summary>The ranges Cloudflare publishes, in the shape its own documentation example uses.</summary>
    private const string Ranges =
        """{"etag":"a8e453d9d129a3769407127936edfdb0","ipv4_cidrs":["173.245.48.0/20","199.27.128.0/21"],"ipv6_cidrs":["2400:cb00::/32","2a06:98c0::/29"]}""";

    [Fact]
    public async Task ListAsync_reads_the_ranges_from_the_api_root_with_no_account_and_no_zone_in_the_path()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(Ranges);

        var ranges = await harness.Client.IpRanges.ListAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.Equal("/client/v4/ips", harness.LastRequest.Path);
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        Assert.Equal("a8e453d9d129a3769407127936edfdb0", ranges.Etag);
        Assert.Equal(2, ranges.Ipv4Cidrs!.Count);
        Assert.Equal("173.245.48.0/20", ranges.Ipv4Cidrs[0]);
        Assert.Equal(2, ranges.Ipv6Cidrs!.Count);
        Assert.Null(ranges.JdCloudCidrs);
        Assert.False(ranges.IncludesJdCloud);
        Assert.True(ranges.AdditionalData is not { Count: > 0 });

        // Every range in one sequence, IPv4 first, is what a firewall rule wants to be handed.
        Assert.Equal(
            new[] { "173.245.48.0/20", "199.27.128.0/21", "2400:cb00::/32", "2a06:98c0::/29" },
            ranges.AllCidrs);
        Assert.Equal("2 IPv4 and 2 IPv6 range(s)", ranges.ToString());
    }

    [Fact]
    public async Task ListAsync_asks_for_the_jd_cloud_ranges_with_the_one_parameter_Cloudflare_publishes()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """{"etag":"a-different-digest","ipv4_cidrs":["173.245.48.0/20"],"ipv6_cidrs":["2400:cb00::/32"],"jdcloud_cidrs":["157.148.0.0/16","2409:8c00::/24"]}""");

        var ranges = await harness.Client.IpRanges.ListAsync(IpRangesListParameters.IncludingJdCloud());

        Assert.Equal("networks=jdcloud", harness.LastRequest.Query.TrimStart('?'));
        Assert.Equal("a-different-digest", ranges.Etag);
        Assert.True(ranges.IncludesJdCloud);
        Assert.Equal(2, ranges.JdCloudCidrs!.Count);

        // The China network's ranges are IPv4 and IPv6 together in one list, so they join the same sequence a
        // caller walks rather than needing a second loop.
        Assert.Equal(4, ranges.AllCidrs.Count());
        Assert.Contains("157.148.0.0/16", ranges.AllCidrs);
        Assert.Equal("1 IPv4 and 1 IPv6 range(s) and 2 JD Cloud", ranges.ToString());
    }

    [Fact]
    public void Contains_answers_whether_an_address_arrived_through_the_cloudflare_edge()
    {
        var ranges = JsonSerializer.Deserialize<CloudflareIpRanges>(Ranges, CloudflareJson.Default)!;

        // Inside each IPv4 range, at both ends of it and in the middle.
        Assert.True(ranges.Contains("173.245.48.0"));
        Assert.True(ranges.Contains("173.245.63.255"));
        Assert.True(ranges.Contains("173.245.55.10"));
        Assert.True(ranges.Contains("199.27.128.5"));
        Assert.True(ranges.Contains("199.27.135.255"));

        // One address past the end of each of them, and one that is nowhere near.
        Assert.False(ranges.Contains("173.245.64.0"));
        Assert.False(ranges.Contains("199.27.136.0"));
        Assert.False(ranges.Contains("8.8.8.8"));

        // IPv6, where a prefix is not a whole number of bytes either: /29 is three bytes and five bits.
        Assert.True(ranges.Contains("2400:cb00:2048::1"));
        Assert.True(ranges.Contains("2a06:98c7::1"));
        Assert.False(ranges.Contains("2401:cb00::1"));
        Assert.False(ranges.Contains("2a06:98ff::1"));

        // Whitespace is what a log line or a header value arrives with, so it is tolerated rather than refused.
        Assert.True(ranges.Contains("  199.27.128.5  "));
    }

    [Fact]
    public void Contains_never_matches_an_address_across_families_and_never_throws_at_a_bad_one()
    {
        // Which family an address belongs to is decided once, by IPAddress.Parse, and ranges of the other
        // family are never compared with it - a 4-byte address and a 16-byte range have no bytes in common to
        // compare, and pretending otherwise would answer true for the wrong reasons.
        var v4Only = Read("""{"ipv4_cidrs":["173.245.48.0/20"]}""");
        Assert.True(v4Only.Contains("173.245.55.10"));
        Assert.False(v4Only.Contains("2400:cb00::1"));

        var v6Only = Read("""{"ipv6_cidrs":["2400:cb00::/32"]}""");
        Assert.True(v6Only.Contains("2400:cb00::1"));
        Assert.False(v6Only.Contains("173.245.55.10"));

        var ranges = JsonSerializer.Deserialize<CloudflareIpRanges>(Ranges, CloudflareJson.Default)!;

        // A filter that threw at a malformed address would be worse than one that answers false, because the
        // address usually came from somewhere the caller does not control.
        Assert.False(ranges.Contains(null));
        Assert.False(ranges.Contains(string.Empty));
        Assert.False(ranges.Contains("   "));
        Assert.False(ranges.Contains("not-an-address"));
        Assert.False(ranges.Contains("199.27.128.0/21"));
        Assert.False(ranges.Contains("999.1.1.1"));
    }

    [Fact]
    public void Contains_covers_the_prefix_lengths_at_both_ends_of_the_range_of_them()
    {
        // /0 is every address of that family and nothing of the other, and /32 is exactly one. Both are legal
        // in an answer and both are the kind of case a bit-shifting implementation gets wrong at the edges.
        var wide = Read("""{"ipv4_cidrs":["0.0.0.0/0"],"ipv6_cidrs":["::/0"]}""");
        Assert.True(wide.Contains("8.8.8.8"));
        Assert.True(wide.Contains("0.0.0.0"));
        Assert.True(wide.Contains("255.255.255.255"));
        Assert.True(wide.Contains("2001:db8::1"));

        // A /0 covers its own family completely and the other family not at all.
        Assert.False(Read("""{"ipv4_cidrs":["0.0.0.0/0"]}""").Contains("2001:db8::1"));
        Assert.False(Read("""{"ipv6_cidrs":["::/0"]}""").Contains("8.8.8.8"));

        var single = Read("""{"ipv4_cidrs":["192.0.2.7/32"]}""");
        Assert.True(single.Contains("192.0.2.7"));
        Assert.False(single.Contains("192.0.2.6"));
        Assert.False(single.Contains("192.0.2.8"));

        // /9 is one whole byte and one bit, so the second byte is the one that decides: 0 to 127 inside, 128
        // and above outside.
        var oddPrefix = Read("""{"ipv4_cidrs":["10.0.0.0/9"]}""");
        Assert.True(oddPrefix.Contains("10.127.255.255"));
        Assert.True(oddPrefix.Contains("10.0.0.1"));
        Assert.False(oddPrefix.Contains("10.128.0.0"));
        Assert.False(oddPrefix.Contains("11.0.0.1"));
    }

    [Fact]
    public void A_range_that_is_not_a_range_is_skipped_rather_than_taken_as_an_error()
    {
        // Cloudflare has never sent one of these, and an answer is not the place to find out that a caller
        // cannot cope: a malformed entry is passed over and the well-formed ones still work.
        var ranges = Read(
            """{"ipv4_cidrs":["not-a-range","10.0.0.0","10.0.0.0/99","10.0.0.0/-1","173.245.48.0/20",""]}""");

        Assert.False(ranges.Contains("10.0.0.1"));
        Assert.True(ranges.Contains("173.245.55.10"));
        Assert.Equal(6, ranges.AllCidrs.Count());
    }

    [Fact]
    public void A_field_this_version_does_not_model_arrives_in_additional_data_rather_than_being_dropped()
    {
        // Older revisions of this endpoint answered the China network's own ranges beside these, and the
        // published schema has moved more than once. Anything unrecognized stays readable, which is how a
        // later release learns to model it from a live run instead of from a guess.
        var ranges = Read(
            """{"etag":"digest","ipv4_cidrs":["173.245.48.0/20"],"china_ipv4_cidrs":["1.2.3.0/24"],"something_new":{"nested":true}}""");

        Assert.NotNull(ranges.AdditionalData);
        Assert.Equal(2, ranges.AdditionalData!.Count);
        Assert.True(ranges.AdditionalData.ContainsKey("china_ipv4_cidrs"));
        Assert.True(ranges.AdditionalData.ContainsKey("something_new"));

        // And an unmodelled list of ranges is not silently folded into the ones a firewall would be built
        // from: AllCidrs is the modelled fields, so what it returns is what the documentation says it returns.
        Assert.Equal(new[] { "173.245.48.0/20" }, ranges.AllCidrs);
        Assert.False(ranges.Contains("1.2.3.4"));
    }

    [Fact]
    public void An_answer_with_no_ranges_at_all_is_still_an_answer()
    {
        var empty = Read("""{"etag":"digest"}""");

        Assert.Empty(empty.AllCidrs);
        Assert.False(empty.Contains("8.8.8.8"));
        Assert.False(empty.IncludesJdCloud);
        Assert.Equal("0 IPv4 and 0 IPv6 range(s)", empty.ToString());
    }

    private static CloudflareIpRanges Read(string json) =>
        JsonSerializer.Deserialize<CloudflareIpRanges>(json, CloudflareJson.Default)!;
}
