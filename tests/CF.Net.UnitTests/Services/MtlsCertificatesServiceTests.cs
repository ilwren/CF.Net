using System.Text.Json;
using CFNet.Exceptions;
using CFNet.Models.MtlsCertificates;
using CFNet.Serialization;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// The mTLS certificates an account uploads. Worth pinning closely because three things here are easy to get
/// wrong: the list is a bare array with a <b>comma-separated</b> type filter rather than a paged one with a
/// repeated parameter, the delete answers the certificate it removed rather than only its id, and the request
/// carries PEM - a text format whose line endings differ by platform and whose mistakes are invisible in a log.
/// </summary>
public class MtlsCertificatesServiceTests
{
    /// <summary>A PEM block with the shape of a real one and the contents of a fixture.</summary>
    private const string CaPem =
        "-----BEGIN CERTIFICATE-----\n" +
        "MIIBszCCAVigAwIBAgIUCfNet0TestCaCertificate0\n" +
        "-----END CERTIFICATE-----";

    /// <summary>A private key in the same shape, for the uploads that need one.</summary>
    private const string KeyPem =
        "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASC\n-----END PRIVATE KEY-----";

    /// <summary>A certificate as an upload answers one, including the <c>updated_at</c> only this answer carries.</summary>
    private const string SavedCertificate =
        """{"id":"mtls-1","ca":true,"certificates":"-----BEGIN CERTIFICATE-----\nMIIBsz\n-----END CERTIFICATE-----","expires_on":"2027-11-07T00:00:00Z","issuer":"CN=cf-net test CA","name":"cf-net test","serial_number":"09:f0:11:22","signature":"SHA256","type":"custom","uploaded_on":"2026-11-07T09:00:00Z","updated_at":"2026-11-07 09:30:00 +0000 UTC"}""";

    [Fact]
    public async Task CreateAsync_posts_the_ca_flag_and_the_pem_and_reads_back_what_Cloudflare_made_of_it()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SavedCertificate);

        var saved = await harness.Client.MtlsCertificates.CreateAsync(
            MtlsCertificateRequest.ForCertificateAuthority(CaPem, "cf-net test"));

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/mtls_certificates",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // The body is the whole of what Cloudflare asks for and nothing more: no private key, because an
        // ordinary BYO-CA upload checks client certificates against the CA and never needs the CA's key.
        Assert.Equal(
            """{"ca":true,"certificates":"-----BEGIN CERTIFICATE-----\nMIIBszCCAVigAwIBAgIUCfNet0TestCaCertificate0\n-----END CERTIFICATE-----","name":"cf-net test"}""",
            harness.LastRequest.Body);

        Assert.Equal("mtls-1", saved.Id);
        Assert.True(saved.Ca);
        Assert.True(saved.IsCertificateAuthority);
        Assert.Equal(MtlsCertificateType.Custom, saved.Type);
        Assert.Equal("CN=cf-net test CA", saved.Issuer);
        Assert.Equal("09:f0:11:22", saved.SerialNumber);
        Assert.Equal("SHA256", saved.Signature);
        Assert.True(saved.AdditionalData is not { Count: > 0 });
        Assert.Equal("mTLS certificate mtls-1 (cf-net test): CA custom, expires 2027-11-07T00:00:00Z", saved.ToString());
    }

    [Fact]
    public async Task The_dates_a_certificate_carries_are_parsed_whatever_shape_Cloudflare_wrote_them_in()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SavedCertificate);

        var saved = await harness.Client.MtlsCertificates.CreateAsync(
            MtlsCertificateRequest.ForCertificateAuthority(CaPem));

        Assert.Equal(new DateTimeOffset(2027, 11, 7, 0, 0, 0, TimeSpan.Zero), saved.ExpiresOnUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero), saved.UploadedOnUtc);

        // updated_at arrives in the layout Go's time package prints - "2026-11-07 09:30:00 +0000 UTC" - which
        // is the third shape this library has met from one API and the reason the raw string stays readable
        // beside the parsed value.
        Assert.Equal("2026-11-07 09:30:00 +0000 UTC", saved.UpdatedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 9, 30, 0, TimeSpan.Zero), saved.UpdatedAtUtc);

        // A certificate that carries no dates at all is still a certificate: null in, null out, no throw.
        harness.RespondWithResult("""{"id":"mtls-2","ca":false,"type":"gateway_managed"}""");
        var bare = await harness.Client.MtlsCertificates.GetAsync("mtls-2");
        Assert.Null(bare!.ExpiresOnUtc);
        Assert.Null(bare.UploadedOnUtc);
        Assert.Null(bare.UpdatedAtUtc);
        Assert.False(bare.IsCertificateAuthority);
        Assert.Equal(MtlsCertificateType.GatewayManaged, bare.Type);
        Assert.Equal("mTLS certificate mtls-2: leaf gateway_managed, expires (no expiry)", bare.ToString());
    }

    [Fact]
    public async Task CreateAsync_settles_windows_line_endings_in_the_pem_before_sending_it()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SavedCertificate);

        var windowsPem = CaPem.Replace("\n", "\r\n", StringComparison.Ordinal);
        var windowsKey = KeyPem.Replace("\n", "\r\n", StringComparison.Ordinal);

        await harness.Client.MtlsCertificates.CreateAsync(
            MtlsCertificateRequest.ForLeafCertificate(windowsPem, windowsKey, "cf-net leaf"));

        // PEM is a text format whose lines Cloudflare parses, and a carriage return inside one is invisible in
        // a log and tedious to diagnose. Every producer of PEM on Windows writes CRLF, so the request is
        // normalized on the way through - a deliberate change, and documented as one.
        var body = harness.LastRequest.Body!;
        Assert.DoesNotContain("\\r", body, StringComparison.Ordinal);
        Assert.Contains("-----BEGIN CERTIFICATE-----\\n", body, StringComparison.Ordinal);
        Assert.Contains("-----BEGIN PRIVATE KEY-----\\n", body, StringComparison.Ordinal);
        Assert.Equal(
            """{"ca":false,"certificates":"-----BEGIN CERTIFICATE-----\nMIIBszCCAVigAwIBAgIUCfNet0TestCaCertificate0\n-----END CERTIFICATE-----","name":"cf-net leaf","private_key":"-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASC\n-----END PRIVATE KEY-----"}""",
            body);
    }

    [Fact]
    public async Task CreateAsync_reports_every_reason_it_would_be_refused_before_spending_the_call()
    {
        using var harness = CloudflareHarness.Create();

        // Nothing at all: both required fields missing, and both reported in one exception.
        var empty = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.MtlsCertificates.CreateAsync(new MtlsCertificateRequest()));
        Assert.Contains(empty.Problems, problem => problem.Contains("CA certificate or a leaf", StringComparison.Ordinal));
        Assert.Contains(empty.Problems, problem => problem.Contains("needs the certificate itself", StringComparison.Ordinal));
        Assert.Equal(2, empty.Problems.Count);

        // A DER blob or a file name where PEM belongs is the mistake worth catching locally: Cloudflare's
        // refusal says the certificate is invalid, and not that it was never PEM to begin with.
        var notPem = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.MtlsCertificates.CreateAsync(
                MtlsCertificateRequest.ForCertificateAuthority("MIIBszCCAVigAwIBAgIUCfNet")));
        Assert.Contains(notPem.Problems, problem => problem.Contains("has to be PEM", StringComparison.Ordinal));

        // A key that is not PEM beside a certificate that is, so the one problem is the key's.
        var notPemKey = await Assert.ThrowsAsync<CloudflareConfigurationException>(
            () => harness.Client.MtlsCertificates.CreateAsync(
                MtlsCertificateRequest.ForLeafCertificate(CaPem, "not-a-key")));
        var problem = Assert.Single(notPemKey.Problems);
        Assert.Contains("private key has to be PEM", problem, StringComparison.Ordinal);

        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public async Task ListAsync_reads_a_bare_array_and_filters_by_types_separated_with_commas()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult($"[{SavedCertificate}]");

        var certificates = await harness.Client.MtlsCertificates.ListAsync(
            MtlsCertificateListParameters.OfTypes(MtlsCertificateType.Custom, MtlsCertificateType.GatewayManaged));

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/mtls_certificates",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // Cloudflare's reference says multiple types can be comma-separated, so it is one parameter with one
        // comma in it - which the query builder percent-escapes, and which decodes back to the comma asked for.
        Assert.Equal("type=custom%2Cgateway_managed", harness.LastRequest.Query.TrimStart('?'));

        var certificate = Assert.Single(certificates);
        Assert.Equal("mtls-1", certificate.Id);

        // No paging is published for this path, so there is no page or per_page to send and nothing to walk.
        harness.RespondWithResult($"[{SavedCertificate}]");
        await harness.Client.MtlsCertificates.ListAsync();
        Assert.Equal(string.Empty, harness.LastRequest.Query);
    }

    [Fact]
    public async Task ListAsync_answers_an_empty_list_for_an_account_that_has_uploaded_nothing()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");
        harness.RespondWithResult("null");

        Assert.Empty(await harness.Client.MtlsCertificates.ListAsync());
        Assert.Empty(await harness.Client.MtlsCertificates.ListAsync());
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task GetAsync_reads_one_certificate_and_answers_null_for_one_that_is_not_there()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SavedCertificate);
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 10007, "Certificate not found");

        var certificate = await harness.Client.MtlsCertificates.GetAsync("mtls-1");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/mtls_certificates/mtls-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("mtls-1", certificate!.Id);

        // Removing something that is already gone is what a cleanup block does twice, so the second attempt
        // answers null instead of throwing at it.
        Assert.Null(await harness.Client.MtlsCertificates.GetAsync("mtls-404"));
    }

    [Fact]
    public async Task DeleteAsync_answers_with_the_certificate_it_removed_rather_than_only_its_id()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(SavedCertificate);
        harness.RespondWithError(System.Net.HttpStatusCode.NotFound, 10007, "Certificate not found");

        var removed = await harness.Client.MtlsCertificates.DeleteAsync("mtls-1");

        Assert.Equal(HttpMethod.Delete, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/mtls_certificates/mtls-1",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal("mtls-1", removed!.Id);
        Assert.Equal("cf-net test", removed.Name);
        Assert.Equal("mTLS certificate mtls-1 (cf-net test): CA custom, expires 2027-11-07T00:00:00Z", removed.ToString());

        Assert.Null(await harness.Client.MtlsCertificates.DeleteAsync("mtls-404"));
    }

    [Fact]
    public async Task ListAssociationsAsync_reads_the_services_a_certificate_is_in_use_by()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"service":"example.com","status":"active"},{"service":"access-app-1","status":"pending","something_new":true}]""");

        var associations = await harness.Client.MtlsCertificates.ListAssociationsAsync("mtls-1");

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/mtls_certificates/mtls-1/associations",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        Assert.Equal(2, associations.Count);
        Assert.Equal("example.com", associations[0].Service);
        Assert.Equal("active", associations[0].Status);
        Assert.Equal("example.com: active", associations[0].ToString());
        Assert.True(associations[0].AdditionalData is not { Count: > 0 });

        // Cloudflare publishes neither the service names nor the statuses, so both stay as they arrive and an
        // unrecognized field beside them is kept rather than dropped.
        Assert.True(associations[1].AdditionalData!.ContainsKey("something_new"));

        // An empty answer is the useful one: it is what says a certificate can be removed safely.
        harness.RespondWithResult("[]");
        Assert.Empty(await harness.Client.MtlsCertificates.ListAssociationsAsync("mtls-2"));
    }

    [Fact]
    public async Task A_certificate_type_Cloudflare_invents_reads_as_unknown_and_is_refused_on_the_way_out()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("""{"id":"mtls-9","ca":true,"type":"quantum_managed"}""");

        var certificate = await harness.Client.MtlsCertificates.GetAsync("mtls-9");

        Assert.Equal(MtlsCertificateType.Unknown, certificate!.Type);
        Assert.Equal("mTLS certificate mtls-9: CA an unpublished type, expires (no expiry)", certificate.ToString());

        // Writing is strict where reading is forgiving: sending a word Cloudflare has not published would ask
        // for something nobody can say what it does.
        Assert.Throws<InvalidOperationException>(
            () => CloudflareEnumConverter<MtlsCertificateType>.ToWire(MtlsCertificateType.Unknown));
        Assert.Equal("gateway_managed", CloudflareEnumConverter<MtlsCertificateType>.ToWire(MtlsCertificateType.GatewayManaged));
        Assert.Equal("access_managed", CloudflareEnumConverter<MtlsCertificateType>.ToWire(MtlsCertificateType.AccessManaged));

        // And an unknown type in a filter is dropped rather than sent, so a caller who filters by one gets
        // every certificate instead of a refused query - which is what not filtering means anyway.
        harness.RespondWithResult("[]");
        await harness.Client.MtlsCertificates.ListAsync(MtlsCertificateListParameters.OfTypes(MtlsCertificateType.Unknown));
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        harness.RespondWithResult("[]");
        await harness.Client.MtlsCertificates.ListAsync(
            MtlsCertificateListParameters.OfTypes(MtlsCertificateType.AccessManaged, MtlsCertificateType.Unknown));
        Assert.Equal("type=access_managed", harness.LastRequest.Query.TrimStart('?'));

        harness.RespondWithResult("[]");
        await harness.Client.MtlsCertificates.ListAsync(MtlsCertificateListParameters.UploadedOnly());
        Assert.Equal("type=custom", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public void The_upload_factories_say_what_they_would_upload_without_printing_any_of_the_material()
    {
        var ca = MtlsCertificateRequest.ForCertificateAuthority(CaPem, "cf-net test");
        Assert.Equal("mTLS upload of a CA certificate (cf-net test)", ca.ToString());

        var leaf = MtlsCertificateRequest.ForLeafCertificate(CaPem, KeyPem);
        Assert.Equal("mTLS upload of a leaf certificate, with its private key", leaf.ToString());

        // A description that ended up in a log line, an exception message or a test failure should never carry
        // certificate material with it, so ToString names the upload and stops there.
        Assert.DoesNotContain("BEGIN CERTIFICATE", ca.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", leaf.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("MIIBsz", ca.ToString(), StringComparison.Ordinal);

        var unstated = new MtlsCertificateRequest { Certificates = CaPem };
        Assert.Equal("mTLS upload of a certificate of unstated role", unstated.ToString());
    }
}
