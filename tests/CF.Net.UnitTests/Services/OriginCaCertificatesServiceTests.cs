using CFNet.Exceptions;
using CFNet.Models.OriginCa;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// Origin CA certificates: <c>/accounts/{account_id}/origin_ca_certificates</c>. Worth pinning closely because
/// three things here are easy to get wrong - the list cannot be called without a zone, the id is an x509 serial
/// number rather than a UUID, and both the CSR and the certificate are newline-encoded PEM, which is line
/// endings a caller on Windows will not have written.
/// </summary>
public class OriginCaCertificatesServiceTests
{
    /// <summary>A CSR in the shape <c>PemEncoding.Write</c> produces, short enough to pin exactly.</summary>
    private const string Csr =
        "-----BEGIN CERTIFICATE REQUEST-----\nMIIBdz\n-----END CERTIFICATE REQUEST-----\n";

    /// <summary>One signed certificate, as a create or a get answers it.</summary>
    private const string SignedCertificate =
        """{"id":"1234567890","certificate":"-----BEGIN CERTIFICATE-----\nMIIC\n-----END CERTIFICATE-----\n","csr":"-----BEGIN CERTIFICATE REQUEST-----\nMIIBdz\n-----END CERTIFICATE REQUEST-----\n","expires_on":"2027-11-07T10:00:00Z","hostnames":["origin.example.com","*.example.com"],"request_type":"origin-rsa","requested_validity":365}""";

    [Fact]
    public async Task CreateAsync_posts_the_csr_the_hostnames_and_the_request_type()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SignedCertificate);

        var signed = await harness.Client.OriginCaCertificates.CreateAsync(
            OriginCaCertificateRequest.ForRsa(
                Csr,
                new[] { "origin.example.com", "*.example.com" },
                OriginCaCertificateValidities.OneYear));

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/origin_ca_certificates",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // No zone_id, because this is not a keyless request and Cloudflare ignores the field for the others -
        // and no wrapper: the CSR, the hostnames and the request type are the whole body.
        Assert.Equal(
            """{"csr":"-----BEGIN CERTIFICATE REQUEST-----\nMIIBdz\n-----END CERTIFICATE REQUEST-----\n","hostnames":["origin.example.com","*.example.com"],"request_type":"origin-rsa","requested_validity":365}""",
            harness.LastRequest.Body);

        // The id is the certificate's x509 serial number, which is what every other method takes.
        Assert.Equal("1234567890", signed.Id);
        Assert.Equal(OriginCaCertificateRequestType.OriginRsa, signed.RequestType);
        Assert.Equal(365, signed.RequestedValidity);
        Assert.Equal(
            new[] { "origin.example.com", "*.example.com" },
            signed.Hostnames!);
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", signed.Certificate, StringComparison.Ordinal);
        Assert.Equal(Csr, signed.Csr);
        Assert.Equal(new DateTimeOffset(2027, 11, 7, 10, 0, 0, TimeSpan.Zero), signed.ExpiresOnUtc);
        Assert.True(signed.AdditionalData is not { Count: > 0 });
        Assert.Equal(
            "origin CA certificate 1234567890 for origin.example.com, *.example.com, 365 day(s), " +
            "expires 2027-11-07T10:00:00Z",
            signed.ToString());
    }

    [Fact]
    public async Task CreateAsync_settles_the_line_endings_of_a_csr_written_on_windows()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SignedCertificate);

        var request = OriginCaCertificateRequest.ForEcc(
            Csr.Replace("\n", "\r\n", StringComparison.Ordinal),
            new[] { "origin.example.com" });

        // The factory settles them, so a CSR from a Windows tool is sent the way Cloudflare asks for it.
        Assert.DoesNotContain("\r", request.Csr, StringComparison.Ordinal);

        await harness.Client.OriginCaCertificates.CreateAsync(request);

        Assert.DoesNotContain(@"\r", harness.LastRequest.Body);
        Assert.Contains(@"\n", harness.LastRequest.Body);
        Assert.Contains(@"""request_type"":""origin-ecc""", harness.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_settles_the_line_endings_of_a_csr_it_was_handed_directly()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SignedCertificate);

        // Setting the properties by hand skips the factory, so the validation the service runs before sending
        // is what settles them - which is why it is allowed to change the request rather than only judge it.
        var request = new OriginCaCertificateRequest
        {
            Csr = Csr.Replace("\n", "\r\n", StringComparison.Ordinal),
            Hostnames = new List<string> { "origin.example.com" },
            RequestType = OriginCaCertificateRequestType.OriginRsa,
        };

        await harness.Client.OriginCaCertificates.CreateAsync(request);

        Assert.DoesNotContain("\r", request.Csr, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\r", harness.LastRequest.Body);
    }

    [Fact]
    public async Task CreateAsync_refuses_a_keyless_request_that_names_no_zone_before_sending_it()
    {
        using var harness = CloudflareHarness.Create();

        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.OriginCaCertificates.CreateAsync(
                OriginCaCertificateRequest.ForKeyless(Csr, new[] { "origin.example.com" }, "   ")));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListAsync_sends_the_zone_it_cannot_do_without_and_reads_the_page()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithPage($"[{SignedCertificate}]", 2, 3, 5, 8);

        var page = await harness.Client.OriginCaCertificates.ListAsync(
            "zone-5678",
            new OriginCaCertificateListParameters { Page = 2, PerPage = 5 });

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/origin_ca_certificates",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal(
            "zone_id=zone-5678&page=2&per_page=5",
            harness.LastRequest.Query.TrimStart('?'));

        var certificate = Assert.Single(page.Items);
        Assert.Equal("1234567890", certificate.Id);
        Assert.Equal(2, page.Info!.Page);
        Assert.Equal(5, page.Info.PerPage);
        Assert.Equal(3, page.Info.TotalPages);
        Assert.Equal(5, page.Info.Count);
        Assert.Equal(8, page.Info.TotalCount);
        Assert.True(page.HasMore);
        Assert.Equal(3, page.NextPage);
    }

    [Fact]
    public async Task ListAsync_refuses_a_call_with_no_zone_before_sending_it()
    {
        using var harness = CloudflareHarness.Create();

        // The endpoint documents zone_id as required, and a list of every certificate on an account is not a
        // thing it answers, so the library says so rather than sending a call that would come back refused.
        await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.OriginCaCertificates.ListAsync(" "));

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListAllAsync_walks_the_pages_until_Cloudflare_says_there_are_no_more()
    {
        using var harness = CloudflareHarness.Create();
        // Two pages, and the second is the last one Cloudflare reports, so the walk stops there rather than
        // on an empty page.
        var second = SignedCertificate.Replace("\"id\":\"1234567890\"", "\"id\":\"2\"", StringComparison.Ordinal);
        var third = SignedCertificate.Replace("\"id\":\"1234567890\"", "\"id\":\"3\"", StringComparison.Ordinal);
        harness.RespondWithPage("[" + SignedCertificate + "," + second + "]", 1, 2, 2, 3);
        harness.RespondWithPage("[" + third + "]", 2, 2, 1, 3);

        var ids = new List<string?>();
        await foreach (var certificate in harness.Client.OriginCaCertificates.ListAllAsync("zone-5678"))
        {
            ids.Add(certificate.Id);
        }

        Assert.Equal(new string?[] { "1234567890", "2", "3" }, ids);
        Assert.Equal(2, harness.RequestCount);
        Assert.Equal("page=2", harness.LastRequest.Query.TrimStart('?').Split('&')[^1]);
    }

    [Fact]
    public async Task GetAsync_reads_one_certificate_and_answers_null_for_one_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SignedCertificate);
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 10007, "Certificate not found");

        var certificate = await harness.Client.OriginCaCertificates.GetAsync("1234567890");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/origin_ca_certificates/1234567890",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("1234567890", certificate!.Id);

        // A revoked certificate is not in this resource any more, and a serial number Cloudflare has never
        // signed is not either, so both read as a null rather than as an exception a caller has to catch.
        Assert.Null(await harness.Client.OriginCaCertificates.GetAsync("9999999999"));
    }

    [Fact]
    public async Task RevokeAsync_answers_with_the_serial_number_and_the_moment_it_stopped_being_trusted()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"1234567890","revoked_at":"2026-11-07T12:34:56Z"}""");

        var revoked = await harness.Client.OriginCaCertificates.RevokeAsync("1234567890");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/origin_ca_certificates/1234567890",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("1234567890", revoked.Id);
        Assert.Equal("2026-11-07T12:34:56Z", revoked.RevokedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 12, 34, 56, TimeSpan.Zero), revoked.RevokedAtUtc);
        Assert.True(revoked.AdditionalData is not { Count: > 0 });
        Assert.Equal("revoked origin CA certificate 1234567890 at 2026-11-07T12:34:56Z", revoked.ToString());
    }

    [Fact]
    public void EnsureValid_names_every_way_a_request_can_be_wrong_at_once()
    {
        var request = new OriginCaCertificateRequest
        {
            Csr = "not a csr at all",
            Hostnames = new List<string>
            {
                string.Empty,
                "*.*.example.com",
                "foo.*.example.com",
                "*.com",
                "no-dot",
                "spaced name.example.com",
            },
            RequestType = OriginCaCertificateRequestType.KeylessCertificate,
            RequestedValidity = 0,
        };

        var exception = Assert.Throws<CloudflareConfigurationException>(() => request.EnsureValid());

        Assert.Contains(exception.Problems, problem => problem.Contains("CSR is PEM", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("cannot be empty", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("2 wildcards", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("interior wildcard", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("multi-label domain", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("no dot in it", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("whitespace", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("keyless servers", StringComparison.Ordinal));
        Assert.Contains(exception.Problems, problem => problem.Contains("not a length of time", StringComparison.Ordinal));

        // One for the CSR's shape, six for the six hostnames, one for the missing zone and one for the
        // validity: naming everything at once is the point, so the count is pinned too.
        Assert.Equal(9, exception.Problems.Count);

        // And a request with nothing in the CSR at all is told that first, because there is no key in it to
        // sign - which is the one problem this library cannot work around for a caller.
        var noCsr = OriginCaCertificateRequest.ForEcc("   ", new[] { "example.com" });
        var missing = Assert.Throws<CloudflareConfigurationException>(() => noCsr.EnsureValid());
        Assert.Contains(missing.Problems, problem => problem.Contains("needs a CSR", StringComparison.Ordinal));
        Assert.Single(missing.Problems);
    }

    [Fact]
    public void A_request_written_in_the_grammar_Cloudflare_publishes_passes_validation()
    {
        // Every hostname shape the documentation calls acceptable, including a wildcard over a multi-label
        // domain and a punycode name, which Cloudflare says it converts rather than refuses.
        var request = OriginCaCertificateRequest.ForRsa(
            Csr,
            new[]
            {
                "example.com",
                "origin.example.com",
                "*.example.com",
                "*.deep.sub.example.com",
                "xn--r8jz45g.example.com",
            },
            OriginCaCertificateValidities.FifteenYears);

        request.EnsureValid();

        Assert.Equal(OriginCaCertificateRequestType.OriginRsa, request.RequestType);
        Assert.Equal(5475, request.RequestedValidity);
        Assert.Null(request.ZoneId);
    }

    [Fact]
    public void PublishedValidities_are_the_day_counts_Cloudflare_publishes_today()
    {
        Assert.Equal(
            new[] { 7, 30, 90, 365, 730, 1095, 5475 },
            OriginCaCertificateRequest.PublishedValidities());

        // A number outside that list is still sent, because the list has changed over the life of this
        // endpoint and a library that refused 2190 would be refusing a request Cloudflare might accept.
        var longer = OriginCaCertificateRequest.ForRsa(Csr, new[] { "example.com" }, 2190);
        longer.EnsureValid();
        Assert.Equal(2190, longer.RequestedValidity);
    }

    [Fact]
    public void The_factories_build_the_request_type_they_are_named_for()
    {
        var rsa = OriginCaCertificateRequest.ForRsa(Csr, new[] { "a.example.com" });
        Assert.Equal(OriginCaCertificateRequestType.OriginRsa, rsa.RequestType);
        Assert.Null(rsa.RequestedValidity);

        var ecc = OriginCaCertificateRequest.ForEcc(Csr, new[] { "b.example.com" }, 90);
        Assert.Equal(OriginCaCertificateRequestType.OriginEcc, ecc.RequestType);
        Assert.Equal(90, ecc.RequestedValidity);

        var keyless = OriginCaCertificateRequest.ForKeyless(Csr, new[] { "c.example.com" }, "zone-5678");
        Assert.Equal(OriginCaCertificateRequestType.KeylessCertificate, keyless.RequestType);
        Assert.Equal("zone-5678", keyless.ZoneId);
        keyless.EnsureValid();

        Assert.Equal(new[] { "c.example.com" }, keyless.Hostnames!);

        // Nothing signed yet, and nothing to say about: the ToString of an empty model is what a log line
        // looks like before the answer arrives.
        Assert.Equal(
            "origin CA certificate (no id yet) for no hostnames, an unpublished validity",
            new OriginCaCertificate().ToString());
        Assert.Equal(
            "revoked origin CA certificate (no id)",
            new OriginCaCertificateRevocation().ToString());
    }

    [Fact]
    public async Task A_request_type_Cloudflare_invents_reads_as_unknown_and_is_refused_on_the_way_out()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"7","request_type":"origin-dsa"}""");

        var certificate = await harness.Client.OriginCaCertificates.GetAsync("7");

        // Reading is forgiving, so a word this version has not met becomes Unknown rather than an exception
        // that would take the whole certificate down with it.
        Assert.Equal(OriginCaCertificateRequestType.Unknown, certificate!.RequestType);
        Assert.Equal("7", certificate.Id);
        Assert.Null(certificate.Hostnames);
        Assert.Equal(
            "origin CA certificate 7 for no hostnames, an unpublished validity",
            certificate.ToString());

        // Writing is not: sending a word Cloudflare has never published would silently ask for the wrong kind
        // of certificate, so that throws instead.
        Assert.Throws<InvalidOperationException>(
            () => CloudflareEnumConverter<OriginCaCertificateRequestType>.ToWire(
                OriginCaCertificateRequestType.Unknown));
    }
}
