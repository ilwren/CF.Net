using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.Ssl;

/// <summary>
/// A custom hostname - a hostname on someone else's domain that points at this zone and is served over
/// HTTPS by Cloudflare. This is SSL for SaaS: <c>/zones/{zone_id}/custom_hostnames</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every field the schema defines is modelled, including the two verification objects and the whole
/// <see cref="Ssl"/> sub-object. Anything Cloudflare adds later lands in <see cref="AdditionalData"/>
/// rather than being dropped.
/// </para>
/// <para>
/// Two of these fields answer the question people actually ask, which is "why is my customer's hostname
/// not working yet": <see cref="Status"/> says how far the hostname itself has got, and
/// <see cref="CustomHostnameSsl.Status"/> says how far its certificate has got. They move independently,
/// and <see cref="VerificationErrors"/> plus
/// <see cref="CustomHostnameSsl.ValidationRecords"/> say what is still owed.
/// </para>
/// </remarks>
public sealed class CustomHostname
{
    /// <summary>Custom hostname identifier tag, 36 characters.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The custom hostname that points at this zone via CNAME, at most 255 characters.</summary>
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    /// <summary>When the hostname was created.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Per-hostname key/value metadata, which is how a SaaS provider tags its own customers.</summary>
    [JsonPropertyName("custom_metadata")]
    public Dictionary<string, string>? CustomMetadata { get; set; }

    /// <summary>
    /// The origin this hostname should be routed to, which has to be a hostname already present in the
    /// zone as an A, AAAA or CNAME record.
    /// </summary>
    [JsonPropertyName("custom_origin_server")]
    public string? CustomOriginServer { get; set; }

    /// <summary>
    /// SNI sent to the custom origin during the TLS handshake. The literal
    /// <c>:request_host_header:</c> makes Cloudflare send the request's Host header instead. Not
    /// configurable together with the default or fallback origin.
    /// </summary>
    [JsonPropertyName("custom_origin_sni")]
    public string? CustomOriginSni { get; set; }

    /// <summary>The DNS record that proves the customer controls this hostname.</summary>
    [JsonPropertyName("ownership_verification")]
    public CustomHostnameOwnershipVerification? OwnershipVerification { get; set; }

    /// <summary>The HTTP token that proves it instead, for customers who cannot add a DNS record.</summary>
    [JsonPropertyName("ownership_verification_http")]
    public CustomHostnameOwnershipVerificationHttp? OwnershipVerificationHttp { get; set; }

    /// <summary>Certificate state for this hostname.</summary>
    [JsonPropertyName("ssl")]
    public CustomHostnameSsl? Ssl { get; set; }

    /// <summary>
    /// Activation status, one of <see cref="CustomHostnameStatuses"/>. Kept as the string it arrived in,
    /// because Cloudflare defines more statuses than any caller needs to branch on.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Why ownership verification has not succeeded, in Cloudflare's own words.</summary>
    [JsonPropertyName("verification_errors")]
    public List<string>? VerificationErrors { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Hostname ?? Id ?? string.Empty;
}

/// <summary>
/// The <c>ssl</c> object of a <see cref="CustomHostname"/>: what certificate Cloudflare has for the
/// hostname, how it is being validated, and what went wrong if it is not.
/// </summary>
public sealed class CustomHostnameSsl
{
    /// <summary>Custom hostname SSL identifier tag, 36 characters.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>How the chain is assembled, one of <see cref="SslBundleMethods"/>.</summary>
    [JsonPropertyName("bundle_method")]
    public string? BundleMethod { get; set; }

    /// <summary>The authority that issued or will issue the certificate.</summary>
    [JsonPropertyName("certificate_authority")]
    public string? CertificateAuthority { get; set; }

    /// <summary>The uploaded certificate, when the customer supplied their own.</summary>
    [JsonPropertyName("custom_certificate")]
    public string? CustomCertificate { get; set; }

    /// <summary>Identifier of the custom CSR that was used, if any.</summary>
    [JsonPropertyName("custom_csr_id")]
    public string? CustomCsrId { get; set; }

    /// <summary>The key for an uploaded certificate.</summary>
    [JsonPropertyName("custom_key")]
    public string? CustomKey { get; set; }

    /// <summary>When an uploaded certificate expires.</summary>
    [JsonPropertyName("expires_on")]
    public DateTimeOffset? ExpiresOn { get; set; }

    /// <summary>Hostnames covered by an uploaded certificate.</summary>
    [JsonPropertyName("hosts")]
    public List<string>? Hosts { get; set; }

    /// <summary>Issuer of an uploaded certificate.</summary>
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    /// <summary>Domain control validation method, one of <see cref="CustomHostnameDcvMethods"/>.</summary>
    [JsonPropertyName("method")]
    public string? Method { get; set; }

    /// <summary>Serial number of an uploaded certificate.</summary>
    [JsonPropertyName("serial_number")]
    public string? SerialNumber { get; set; }

    /// <summary>Per-hostname TLS settings.</summary>
    [JsonPropertyName("settings")]
    public CustomHostnameSslSettings? Settings { get; set; }

    /// <summary>Signature of an uploaded certificate.</summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>
    /// Certificate status, one of <see cref="CustomHostnameSslStatuses"/>. This is the field to poll
    /// while waiting for a certificate: it walks from <c>initializing</c> through
    /// <c>pending_validation</c> and <c>pending_issuance</c> to <c>active</c>, and the
    /// <c>*_timed_out</c> values are where it stops when nobody answers the challenge.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Level of validation, one of <see cref="CustomHostnameValidationTypes"/>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>When an uploaded certificate was uploaded.</summary>
    [JsonPropertyName("uploaded_on")]
    public DateTimeOffset? UploadedOn { get; set; }

    /// <summary>Domain validation errors reported by the certificate authority.</summary>
    [JsonPropertyName("validation_errors")]
    public List<CustomHostnameSslValidationError>? ValidationErrors { get; set; }

    /// <summary>
    /// The challenges themselves: which TXT record to create, which URL to serve which body from, or which
    /// mailbox will receive the email. This is what a SaaS provider shows its customer.
    /// </summary>
    [JsonPropertyName("validation_records")]
    public List<CustomHostnameSslValidationRecord>? ValidationRecords { get; set; }

    /// <summary>Whether the certificate covers a wildcard hostname.</summary>
    [JsonPropertyName("wildcard")]
    public bool? Wildcard { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Status ?? string.Empty;
}

/// <summary>
/// TLS settings that apply to one custom hostname rather than to the whole zone. The same five fields are
/// read back and written, so one type serves both directions.
/// </summary>
public sealed class CustomHostnameSslSettings
{
    /// <summary>Allowlist of ciphers for TLS termination, in BoringSSL format.</summary>
    [JsonPropertyName("ciphers")]
    public List<string>? Ciphers { get; set; }

    /// <summary>Whether Early Hints is enabled: <c>"on"</c> or <c>"off"</c>.</summary>
    [JsonPropertyName("early_hints")]
    public string? EarlyHints { get; set; }

    /// <summary>Whether HTTP/2 is enabled: <c>"on"</c> or <c>"off"</c>.</summary>
    [JsonPropertyName("http2")]
    public string? Http2 { get; set; }

    /// <summary>Minimum TLS version, <c>"1.0"</c> to <c>"1.3"</c>.</summary>
    [JsonPropertyName("min_tls_version")]
    public string? MinTlsVersion { get; set; }

    /// <summary>Whether TLS 1.3 is enabled: <c>"on"</c> or <c>"off"</c>.</summary>
    [JsonPropertyName("tls_1_3")]
    public string? Tls13 { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>One domain validation error reported by the certificate authority.</summary>
public sealed class CustomHostnameSslValidationError
{
    /// <summary>The error, in the authority's words.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Message ?? string.Empty;
}

/// <summary>
/// One domain control validation challenge. Which fields are populated depends on the method: TXT fills
/// <see cref="TxtName"/> and <see cref="TxtValue"/>, HTTP fills <see cref="HttpUrl"/> and
/// <see cref="HttpBody"/>, email fills <see cref="Emails"/>, and CNAME delegation fills
/// <see cref="Cname"/> and <see cref="CnameTarget"/>.
/// </summary>
public sealed class CustomHostnameSslValidationRecord
{
    /// <summary>The CNAME record hostname for DCV delegation.</summary>
    [JsonPropertyName("cname")]
    public string? Cname { get; set; }

    /// <summary>The CNAME record target for DCV delegation.</summary>
    [JsonPropertyName("cname_target")]
    public string? CnameTarget { get; set; }

    /// <summary>Mailboxes the authority will send the validation email to.</summary>
    [JsonPropertyName("emails")]
    public List<string>? Emails { get; set; }

    /// <summary>The body the authority expects to find at <see cref="HttpUrl"/>.</summary>
    [JsonPropertyName("http_body")]
    public string? HttpBody { get; set; }

    /// <summary>The URL the authority will check.</summary>
    [JsonPropertyName("http_url")]
    public string? HttpUrl { get; set; }

    /// <summary>Status of this individual record.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>The hostname the authority will look for a TXT record on.</summary>
    [JsonPropertyName("txt_name")]
    public string? TxtName { get; set; }

    /// <summary>The TXT record content the authority will look for.</summary>
    [JsonPropertyName("txt_value")]
    public string? TxtValue { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => TxtName ?? HttpUrl ?? Cname ?? Status ?? string.Empty;
}

/// <summary>The DNS record that proves the customer owns the hostname.</summary>
public sealed class CustomHostnameOwnershipVerification
{
    /// <summary>DNS name for the record.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>DNS record type. The schema defines one value: <c>txt</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Content for the record.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Name ?? string.Empty;
}

/// <summary>The HTTP token that proves ownership, for customers who cannot touch their DNS.</summary>
public sealed class CustomHostnameOwnershipVerificationHttp
{
    /// <summary>Token to be served.</summary>
    [JsonPropertyName("http_body")]
    public string? HttpBody { get; set; }

    /// <summary>URL that will be checked, and where the customer should host the token.</summary>
    [JsonPropertyName("http_url")]
    public string? HttpUrl { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => HttpUrl ?? string.Empty;
}

/// <summary>
/// One certificate and key pair in a <c>custom_cert_bundle</c>. Bundling needs one RSA certificate and one
/// ECDSA certificate, which is a condition Cloudflare checks and this library cannot.
/// </summary>
public sealed class CustomHostnameCertificatePair
{
    /// <summary>The uploaded certificate, in PEM.</summary>
    [JsonPropertyName("custom_certificate")]
    public string? CustomCertificate { get; set; }

    /// <summary>The key for it.</summary>
    [JsonPropertyName("custom_key")]
    public string? CustomKey { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Builds one pair.</summary>
    /// <param name="certificate">Certificate, in PEM.</param>
    /// <param name="key">Its key.</param>
    public static CustomHostnameCertificatePair Of(string certificate, string key) =>
        new() { CustomCertificate = certificate, CustomKey = key };

    /// <inheritdoc />
    public override string ToString() => "certificate and key";
}

/// <summary>
/// The <c>ssl</c> half of both the create and the edit body. Which fields matter depends on what you are
/// doing: ordering a Cloudflare-issued certificate needs <see cref="Method"/> and usually
/// <see cref="Type"/>, uploading your own needs <see cref="CustomCertificate"/> and
/// <see cref="CustomKey"/>, and bundling two of yours needs <see cref="CustomCertBundle"/>.
/// </summary>
public sealed class CustomHostnameSslRequest
{
    /// <summary>How to assemble the chain, one of <see cref="SslBundleMethods"/>.</summary>
    [JsonPropertyName("bundle_method")]
    public string? BundleMethod { get; set; }

    /// <summary>
    /// Which authority should issue the certificate, one of
    /// <see cref="CustomHostnameCertificateAuthorities"/>.
    /// </summary>
    [JsonPropertyName("certificate_authority")]
    public string? CertificateAuthority { get; set; }

    /// <summary>
    /// Adds Cloudflare branding, which puts a subdomain of <c>sni.cloudflaressl.com</c> in the
    /// certificate as a second Common Name.
    /// </summary>
    [JsonPropertyName("cloudflare_branding")]
    public bool? CloudflareBranding { get; set; }

    /// <summary>One or two certificate and key pairs to bundle together.</summary>
    [JsonPropertyName("custom_cert_bundle")]
    public List<CustomHostnameCertificatePair>? CustomCertBundle { get; set; }

    /// <summary>An uploaded certificate, in PEM.</summary>
    [JsonPropertyName("custom_certificate")]
    public string? CustomCertificate { get; set; }

    /// <summary>Identifier of a custom CSR to use.</summary>
    [JsonPropertyName("custom_csr_id")]
    public string? CustomCsrId { get; set; }

    /// <summary>The key for an uploaded certificate.</summary>
    [JsonPropertyName("custom_key")]
    public string? CustomKey { get; set; }

    /// <summary>
    /// Domain control validation method, one of <see cref="CustomHostnameDcvMethods"/>. Cloudflare
    /// recommends <c>http</c> when the CNAME is already in place or about to be.
    /// </summary>
    [JsonPropertyName("method")]
    public string? Method { get; set; }

    /// <summary>Per-hostname TLS settings.</summary>
    [JsonPropertyName("settings")]
    public CustomHostnameSslSettings? Settings { get; set; }

    /// <summary>
    /// Level of validation, one of <see cref="CustomHostnameValidationTypes"/>. Domain validation is the
    /// only level this endpoint accepts.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Whether the certificate should cover a wildcard hostname.</summary>
    [JsonPropertyName("wildcard")]
    public bool? Wildcard { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// Builds the SSL body that asks for a Cloudflare-issued certificate validated by
    /// <paramref name="method"/>, which is the common case.
    /// </summary>
    /// <param name="method">One of <see cref="CustomHostnameDcvMethods"/>.</param>
    /// <param name="wildcard">Whether the hostname is a wildcard.</param>
    public static CustomHostnameSslRequest ForValidation(string method, bool wildcard = false) =>
        new()
        {
            Method = method,
            Type = CustomHostnameValidationTypes.DomainValidation,
            Wildcard = wildcard,
        };

    /// <summary>Builds the SSL body that uploads a certificate and key of your own.</summary>
    /// <param name="certificate">Certificate, in PEM.</param>
    /// <param name="key">Its key.</param>
    /// <param name="bundleMethod">How to assemble the chain; omitted means Cloudflare's default.</param>
    public static CustomHostnameSslRequest WithUploadedCertificate(
        string certificate,
        string key,
        string? bundleMethod = null) =>
        new()
        {
            CustomCertificate = certificate,
            CustomKey = key,
            BundleMethod = bundleMethod,
        };

    /// <inheritdoc />
    public override string ToString() => Method ?? (CustomCertificate is null ? "ssl settings" : "uploaded certificate");
}

/// <summary>Body of <c>POST /zones/{zone_id}/custom_hostnames</c>.</summary>
/// <remarks>
/// Creating the hostname is only the start: Cloudflare then needs the customer to prove they control it,
/// through the DNS record in <see cref="CustomHostname.OwnershipVerification"/> or the HTTP token in
/// <see cref="CustomHostname.OwnershipVerificationHttp"/>, and the response says which. With the <c>http</c> method and
/// a CNAME that is not in place yet, the edit call has to be repeated once it is, which is what completes
/// validation.
/// </remarks>
public sealed class CreateCustomHostnameRequest
{
    /// <summary>At most two certificate and key pairs may be bundled.</summary>
    public const int MaxBundleSize = 2;

    /// <summary>The hostname to add, at most 255 characters. Required.</summary>
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    /// <summary>Per-hostname metadata to store alongside it.</summary>
    [JsonPropertyName("custom_metadata")]
    public Dictionary<string, string>? CustomMetadata { get; set; }

    /// <summary>Origin to route this hostname to, which must already exist in the zone's DNS.</summary>
    [JsonPropertyName("custom_origin_server")]
    public string? CustomOriginServer { get; set; }

    /// <summary>SNI to send to that origin, or <c>:request_host_header:</c> to send the Host header.</summary>
    [JsonPropertyName("custom_origin_sni")]
    public string? CustomOriginSni { get; set; }

    /// <summary>Certificate configuration. Omit it and Cloudflare issues a certificate with its defaults.</summary>
    [JsonPropertyName("ssl")]
    public CustomHostnameSslRequest? Ssl { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Builds the request for one hostname.</summary>
    /// <param name="hostname">Hostname to add.</param>
    /// <param name="ssl">Certificate configuration; null means Cloudflare's defaults.</param>
    public static CreateCustomHostnameRequest For(string hostname, CustomHostnameSslRequest? ssl = null) =>
        new() { Hostname = hostname, Ssl = ssl };

    /// <summary>Checks the request against what Cloudflare accepts, before anything is sent.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">The hostname is missing, or the bundle size is wrong.</exception>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Hostname))
        {
            throw new Exceptions.CloudflareConfigurationException(
                "A custom hostname needs the hostname itself.");
        }

        if (Ssl?.CustomCertBundle is { } bundle && bundle.Count is 0 or > MaxBundleSize)
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"A custom certificate bundle holds one or two pairs, and {bundle.Count} were given.");
        }

        if (Ssl?.CustomCertBundle is { } pairs && pairs.Any(pair =>
                string.IsNullOrWhiteSpace(pair.CustomCertificate) || string.IsNullOrWhiteSpace(pair.CustomKey)))
        {
            throw new Exceptions.CloudflareConfigurationException(
                "Every pair in a custom certificate bundle needs both a certificate and a key.");
        }
    }

    /// <inheritdoc />
    public override string ToString() => Hostname ?? string.Empty;
}

/// <summary>
/// Body of <c>PATCH /zones/{zone_id}/custom_hostnames/{custom_hostname_id}</c>.
/// </summary>
/// <remarks>
/// The hostname itself cannot be changed - there is no <c>hostname</c> field here, only in the create body.
/// The call has a second, less obvious use: sending SSL configuration that matches what is already there
/// tells Cloudflare to try domain control validation again, which is how you finish validating an
/// <c>http</c> hostname whose CNAME was not in place when it was created. It is also how the validation
/// method is changed, for example from <c>http</c> to <c>email</c>.
/// </remarks>
public sealed class UpdateCustomHostnameRequest
{
    /// <summary>Replacement per-hostname metadata.</summary>
    [JsonPropertyName("custom_metadata")]
    public Dictionary<string, string>? CustomMetadata { get; set; }

    /// <summary>Replacement origin.</summary>
    [JsonPropertyName("custom_origin_server")]
    public string? CustomOriginServer { get; set; }

    /// <summary>Replacement SNI for that origin.</summary>
    [JsonPropertyName("custom_origin_sni")]
    public string? CustomOriginSni { get; set; }

    /// <summary>Replacement certificate configuration.</summary>
    [JsonPropertyName("ssl")]
    public CustomHostnameSslRequest? Ssl { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// Builds the body that retries domain control validation with <paramref name="method"/>, which is the
    /// nudge an <c>http</c> hostname needs once its CNAME finally resolves.
    /// </summary>
    /// <param name="method">One of <see cref="CustomHostnameDcvMethods"/>.</param>
    public static UpdateCustomHostnameRequest ToRetryValidation(string method) =>
        new() { Ssl = CustomHostnameSslRequest.ForValidation(method) };

    /// <inheritdoc />
    public override string ToString() =>
        Ssl?.Method is { } method ? $"retry validation with {method}" : "custom hostname settings";
}

/// <summary>
/// Query parameters of <c>GET /zones/{zone_id}/custom_hostnames</c>.
/// </summary>
/// <remarks>
/// <para>
/// The three hostname filters are mutually exclusive with each other and with <see cref="Id"/>, and
/// Cloudflare rejects a request that combines them, so only one should be set at a time. They go on the
/// wire as dotted keys - <c>hostname.contain</c>, <c>hostname.exact</c>, <c>hostname.startsWith</c> -
/// which is unusual enough to be worth spelling out.
/// </para>
/// <para>
/// <see cref="PerPage"/> runs from 5 to 1000 here, which is a much wider range than most list endpoints.
/// </para>
/// </remarks>
public sealed class CustomHostnameListParameters : CloudflarePagedParameters
{
    /// <summary>Hostname id to match exactly. Cannot be combined with the hostname filters.</summary>
    public string? Id { get; set; }

    /// <summary>
    /// Filter by issuing authority. The schema accepts <c>google</c>, <c>lets_encrypt</c> and
    /// <c>ssl_com</c> here - not <c>digicert</c>, even though a hostname can be issued by it.
    /// </summary>
    public string? CertificateAuthority { get; set; }

    /// <summary>Filter by custom origin server name.</summary>
    public string? CustomOriginServer { get; set; }

    /// <summary>Sort direction, one of <see cref="CustomHostnameSortDirections"/>.</summary>
    public string? Direction { get; set; }

    /// <summary>Substring match on the hostname. Sent as <c>hostname.contain</c>.</summary>
    public string? HostnameContains { get; set; }

    /// <summary>Fully qualified hostname to match. Sent as <c>hostname.exact</c>.</summary>
    public string? HostnameExact { get; set; }

    /// <summary>Prefix match on the hostname. Sent as <c>hostname.startsWith</c>.</summary>
    public string? HostnameStartsWith { get; set; }

    /// <summary>Filter by activation status, one of <see cref="CustomHostnameStatuses"/>.</summary>
    public string? HostnameStatus { get; set; }

    /// <summary>Field to sort by, one of <see cref="CustomHostnameSortOrders"/>.</summary>
    public string? Order { get; set; }

    /// <summary>
    /// Filter on whether the hostname has SSL enabled. Cloudflare wants <c>1</c> or <c>0</c> here rather
    /// than <c>true</c> or <c>false</c>, which is why this is a nullable bool with a translation.
    /// </summary>
    public bool? Ssl { get; set; }

    /// <summary>Filter by certificate status, one of <see cref="CustomHostnameSslStatuses"/>.</summary>
    public string? SslStatus { get; set; }

    /// <summary>Filter on whether the hostname is a wildcard.</summary>
    public bool? Wildcard { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("id", Id);
        query.AddIfNotEmpty("certificate_authority", CertificateAuthority);
        query.AddIfNotEmpty("custom_origin_server", CustomOriginServer);
        query.AddIfNotEmpty("direction", Direction);
        query.AddIfNotEmpty("hostname.contain", HostnameContains);
        query.AddIfNotEmpty("hostname.exact", HostnameExact);
        query.AddIfNotEmpty("hostname.startsWith", HostnameStartsWith);
        query.AddIfNotEmpty("hostname_status", HostnameStatus);
        query.AddIfNotEmpty("order", Order);
        query.AddIfNotEmpty("ssl_status", SslStatus);
        query.Add("wildcard", Wildcard);

        // This one is 1 and 0 on the wire, not true and false.
        if (Ssl.HasValue)
        {
            query.Add("ssl", Ssl.Value ? 1 : 0);
        }
    }
}

/// <summary>
/// Activation statuses of a custom hostname - the values of both the <c>status</c> field and the
/// <c>hostname_status</c> filter, which the schema defines as the same enumeration.
/// </summary>
public static class CustomHostnameStatuses
{
    /// <summary>The hostname is live.</summary>
    public const string Active = "active";

    /// <summary>Waiting for ownership verification or for its certificate.</summary>
    public const string Pending = "pending";

    /// <summary>Active, and being redeployed.</summary>
    public const string ActiveRedeploying = "active_redeploying";

    /// <summary>The hostname has moved elsewhere.</summary>
    public const string Moved = "moved";

    /// <summary>Deletion has started.</summary>
    public const string PendingDeletion = "pending_deletion";

    /// <summary>Deleted.</summary>
    public const string Deleted = "deleted";

    /// <summary>Blocked, and waiting.</summary>
    public const string PendingBlocked = "pending_blocked";

    /// <summary>Being migrated.</summary>
    public const string PendingMigration = "pending_migration";

    /// <summary>Being provisioned.</summary>
    public const string PendingProvisioned = "pending_provisioned";

    /// <summary>A test hostname waiting to be verified.</summary>
    public const string TestPending = "test_pending";

    /// <summary>A test hostname that is active.</summary>
    public const string TestActive = "test_active";

    /// <summary>A test hostname on a zone apex that is active.</summary>
    public const string TestActiveApex = "test_active_apex";

    /// <summary>A test hostname that is blocked.</summary>
    public const string TestBlocked = "test_blocked";

    /// <summary>A test hostname that failed verification.</summary>
    public const string TestFailed = "test_failed";

    /// <summary>Provisioned.</summary>
    public const string Provisioned = "provisioned";

    /// <summary>Blocked.</summary>
    public const string Blocked = "blocked";

    /// <summary>Every status the schema defines, in the order it defines them.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Active, Pending, ActiveRedeploying, Moved, PendingDeletion, Deleted,
        PendingBlocked, PendingMigration, PendingProvisioned, TestPending, TestActive,
        TestActiveApex, TestBlocked, TestFailed, Provisioned, Blocked,
    };
}

/// <summary>
/// Certificate statuses of a custom hostname - the values of both <c>ssl.status</c> and the
/// <c>ssl_status</c> filter.
/// </summary>
public static class CustomHostnameSslStatuses
{
    /// <summary>Certificate setup has begun.</summary>
    public const string Initializing = "initializing";

    /// <summary>Waiting for the customer to answer the challenge.</summary>
    public const string PendingValidation = "pending_validation";

    /// <summary>Deleted.</summary>
    public const string Deleted = "deleted";

    /// <summary>Validated, waiting to be issued.</summary>
    public const string PendingIssuance = "pending_issuance";

    /// <summary>Issued, waiting to be deployed to the edge.</summary>
    public const string PendingDeployment = "pending_deployment";

    /// <summary>Deletion has started.</summary>
    public const string PendingDeletion = "pending_deletion";

    /// <summary>Expiry has started.</summary>
    public const string PendingExpiration = "pending_expiration";

    /// <summary>Expired.</summary>
    public const string Expired = "expired";

    /// <summary>Deployed and serving.</summary>
    public const string Active = "active";

    /// <summary>Initialising took too long.</summary>
    public const string InitializingTimedOut = "initializing_timed_out";

    /// <summary>Nobody answered the challenge in time.</summary>
    public const string ValidationTimedOut = "validation_timed_out";

    /// <summary>Issuance took too long.</summary>
    public const string IssuanceTimedOut = "issuance_timed_out";

    /// <summary>Deployment took too long.</summary>
    public const string DeploymentTimedOut = "deployment_timed_out";

    /// <summary>Deletion took too long.</summary>
    public const string DeletionTimedOut = "deletion_timed_out";

    /// <summary>Waiting to be cleaned up.</summary>
    public const string PendingCleanup = "pending_cleanup";

    /// <summary>Deployed to staging.</summary>
    public const string StagingDeployment = "staging_deployment";

    /// <summary>Active on staging.</summary>
    public const string StagingActive = "staging_active";

    /// <summary>Being deactivated.</summary>
    public const string Deactivating = "deactivating";

    /// <summary>Not active.</summary>
    public const string Inactive = "inactive";

    /// <summary>A backup certificate was issued.</summary>
    public const string BackupIssued = "backup_issued";

    /// <summary>Deployment is being held.</summary>
    public const string HoldingDeployment = "holding_deployment";

    /// <summary>Every status the schema defines, in the order it defines them.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Initializing, PendingValidation, Deleted, PendingIssuance, PendingDeployment, PendingDeletion,
        PendingExpiration, Expired, Active, InitializingTimedOut, ValidationTimedOut, IssuanceTimedOut,
        DeploymentTimedOut, DeletionTimedOut, PendingCleanup, StagingDeployment, StagingActive,
        Deactivating, Inactive, BackupIssued, HoldingDeployment,
    };
}

/// <summary>Domain control validation methods a custom hostname can use.</summary>
/// <remarks>
/// These overlap with but are not the same set as the certificate pack methods in
/// <see cref="SslValidationMethods"/>: there is no <c>cname</c> here, and the recommended default is
/// <c>http</c> rather than <c>txt</c>.
/// </remarks>
public static class CustomHostnameDcvMethods
{
    /// <summary>Serve a token over HTTP. Recommended once the CNAME is in place.</summary>
    public const string Http = "http";

    /// <summary>Create a TXT record.</summary>
    public const string Txt = "txt";

    /// <summary>
    /// Email the WHOIS contacts of the base domain, plus hostmaster, postmaster, webmaster, admin and
    /// administrator.
    /// </summary>
    public const string Email = "email";

    /// <summary>Every method the schema defines.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Http, Txt, Email };
}

/// <summary>Validation levels for a custom hostname.</summary>
public static class CustomHostnameValidationTypes
{
    /// <summary>Domain validation, the only level this endpoint accepts.</summary>
    public const string DomainValidation = "dv";

    /// <summary>Every level the schema defines.</summary>
    public static readonly IReadOnlyList<string> All = new[] { DomainValidation };
}

/// <summary>Fields a custom hostname list can be sorted by.</summary>
public static class CustomHostnameSortOrders
{
    /// <summary>Sort by SSL.</summary>
    public const string Ssl = "ssl";

    /// <summary>Sort by SSL status.</summary>
    public const string SslStatus = "ssl_status";

    /// <summary>Every field the schema defines.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Ssl, SslStatus };
}

/// <summary>Sort directions for a custom hostname list.</summary>
public static class CustomHostnameSortDirections
{
    /// <summary>Ascending.</summary>
    public const string Ascending = "asc";

    /// <summary>Descending.</summary>
    public const string Descending = "desc";

    /// <summary>Every direction the schema defines.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Ascending, Descending };
}

/// <summary>
/// Certificate authorities that can issue a custom hostname's certificate.
/// </summary>
/// <remarks>
/// This is the shared <c>CertificateCA</c> enumeration, which includes <c>digicert</c>; the authority set
/// for ordering an advanced certificate pack in <see cref="SslCertificateAuthorities"/> does not. The list
/// filter accepts only the three in <see cref="Filterable"/>.
/// </remarks>
public static class CustomHostnameCertificateAuthorities
{
    /// <summary>DigiCert.</summary>
    public const string DigiCert = "digicert";

    /// <summary>Google Trust Services.</summary>
    public const string Google = "google";

    /// <summary>Let's Encrypt.</summary>
    public const string LetsEncrypt = "lets_encrypt";

    /// <summary>SSL.com.</summary>
    public const string SslCom = "ssl_com";

    /// <summary>Every authority the schema defines.</summary>
    public static readonly IReadOnlyList<string> All = new[] { DigiCert, Google, LetsEncrypt, SslCom };

    /// <summary>The subset the list endpoint accepts as a filter.</summary>
    public static readonly IReadOnlyList<string> Filterable = new[] { Google, LetsEncrypt, SslCom };
}
