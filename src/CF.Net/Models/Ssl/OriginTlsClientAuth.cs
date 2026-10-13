using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Ssl;

/// <summary>
/// One hostname's association with a client certificate, as returned by
/// <c>/zones/{zone_id}/origin_tls_client_auth/hostnames</c>: the hostname, the certificate it points at, and
/// two independent statuses.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Status"/> tracks the association and <see cref="CertStatus"/> tracks the certificate, and they
/// move on their own schedule - the same split <c>CustomHostname</c> has between its own status and
/// <c>ssl.status</c>. Both draw on <see cref="OriginTlsClientAuthStatuses"/>.
/// </para>
/// <para>
/// The certificate itself is echoed back in <see cref="Certificate"/> as PEM. <see cref="ToString"/> never
/// prints it: a status line is not a place for key material.
/// </para>
/// </remarks>
public sealed class OriginTlsHostnameAssociation
{
    /// <summary>Identifier of the certificate this hostname is associated with.</summary>
    [JsonPropertyName("cert_id")]
    public string? CertId { get; set; }

    /// <summary>Status of the certificate.</summary>
    [JsonPropertyName("cert_status")]
    public string? CertStatus { get; set; }

    /// <summary>When the certificate was last updated.</summary>
    [JsonPropertyName("cert_updated_at")]
    public DateTimeOffset? CertUpdatedAt { get; set; }

    /// <summary>When the certificate was uploaded.</summary>
    [JsonPropertyName("cert_uploaded_on")]
    public DateTimeOffset? CertUploadedOn { get; set; }

    /// <summary>The client certificate, in PEM. Never printed by <see cref="ToString"/>.</summary>
    [JsonPropertyName("certificate")]
    public string? Certificate { get; set; }

    /// <summary>When the association was created.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Whether this hostname sends the client certificate. <c>null</c> means the association is void - it is
    /// a third state, not a missing one.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>When the certificate expires.</summary>
    [JsonPropertyName("expires_on")]
    public DateTimeOffset? ExpiresOn { get; set; }

    /// <summary>The hostname on the origin the certificate is used for.</summary>
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    /// <summary>Authority that issued the certificate.</summary>
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    /// <summary>Serial number on the uploaded certificate.</summary>
    [JsonPropertyName("serial_number")]
    public string? SerialNumber { get; set; }

    /// <summary>Hash type used for the certificate, for example <c>SHA256WithRSA</c>.</summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>Status of the association.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>When the association was last updated.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Whether the hostname is currently set to present the certificate.</summary>
    [JsonIgnore]
    public bool IsEnabled => Enabled == true;

    /// <summary>
    /// One line of status: the hostname, whether it is enabled, and both statuses. Certificate material is
    /// deliberately absent.
    /// </summary>
    /// <returns>Hostname, enablement and statuses.</returns>
    public override string ToString()
    {
        var enablement = Enabled switch
        {
            true => "enabled",
            false => "disabled",
            _ => "void",
        };

        return Hostname is { Length: > 0 } hostname
            ? $"{hostname} ({enablement}, association {Status ?? "unknown"}, certificate {CertStatus ?? "unknown"})"
            : enablement;
    }
}

/// <summary>
/// One entry of the <c>config</c> array sent to
/// <c>PUT /zones/{zone_id}/origin_tls_client_auth/hostnames</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Enabled"/> has three states and all three mean something.</b> <c>true</c> associates the
/// hostname with the certificate and turns it on, <c>false</c> keeps the association but stops the
/// certificate being sent - even when the zone-level setting is on - and <c>null</c> <b>voids the
/// association</b>, which is what Cloudflare asks for before a certificate can be deleted. Because a null
/// would otherwise be dropped from the JSON, this property is written explicitly, so an entry built without
/// one of <see cref="Enable"/>, <see cref="Disable"/> or <see cref="Void"/> asks for the association to be
/// voided. Use the factories.
/// </para>
/// <para>At most 100 hostnames may be associated with a single certificate.</para>
/// </remarks>
public sealed class OriginTlsHostnameAssociationChange
{
    /// <summary>Identifier of the certificate to associate the hostname with.</summary>
    [JsonPropertyName("cert_id")]
    public string? CertId { get; set; }

    /// <summary>
    /// <c>true</c> to enable, <c>false</c> to disable, <c>null</c> to void the association. Written to the
    /// body even when null, because null is the void state rather than an omission.
    /// </summary>
    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? Enabled { get; set; }

    /// <summary>The hostname on the origin, at most 255 characters.</summary>
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Associates the hostname with the certificate and turns it on.</summary>
    /// <param name="hostname">Hostname on the origin.</param>
    /// <param name="certificateId">Identifier of an uploaded hostname certificate.</param>
    /// <returns>The entry to send.</returns>
    public static OriginTlsHostnameAssociationChange Enable(string hostname, string certificateId) =>
        new() { Hostname = hostname, CertId = certificateId, Enabled = true };

    /// <summary>
    /// Keeps the association but stops the certificate being sent for this hostname, even with the
    /// zone-level setting on.
    /// </summary>
    /// <param name="hostname">Hostname on the origin.</param>
    /// <param name="certificateId">Identifier of an uploaded hostname certificate.</param>
    /// <returns>The entry to send.</returns>
    public static OriginTlsHostnameAssociationChange Disable(string hostname, string certificateId) =>
        new() { Hostname = hostname, CertId = certificateId, Enabled = false };

    /// <summary>
    /// Voids the association by sending <c>enabled: null</c>. Cloudflare requires this before the
    /// certificate the hostname pointed at can be deleted.
    /// </summary>
    /// <param name="hostname">Hostname on the origin.</param>
    /// <param name="certificateId">Identifier of the certificate to disassociate.</param>
    /// <returns>The entry to send.</returns>
    public static OriginTlsHostnameAssociationChange Void(string hostname, string certificateId) =>
        new() { Hostname = hostname, CertId = certificateId, Enabled = null };

    /// <summary>Describes the change without repeating the certificate.</summary>
    /// <returns>Hostname and which of the three states is being asked for.</returns>
    public override string ToString()
    {
        var state = Enabled switch
        {
            true => "enable",
            false => "disable",
            _ => "void",
        };

        return Hostname is { Length: > 0 } hostname ? $"{state} {hostname}" : state;
    }
}

/// <summary>Body of <c>PUT /zones/{zone_id}/origin_tls_client_auth/hostnames</c>.</summary>
/// <remarks>
/// The body is an <b>array</b> under <c>config</c>, so several hostnames can be enabled, disabled or voided
/// in one call, and the response is an array of the associations as they now stand. Note that the path has
/// no hostname in it: the hostname travels in the body.
/// </remarks>
public sealed class UpdateOriginTlsHostnameAssociationsRequest
{
    /// <summary>The associations to apply. Required, and at least one entry.</summary>
    [JsonPropertyName("config")]
    public List<OriginTlsHostnameAssociationChange>? Config { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Builds a body from one or more changes.</summary>
    /// <param name="changes">Entries built with the enable, disable or void factories.</param>
    /// <returns>The request.</returns>
    public static UpdateOriginTlsHostnameAssociationsRequest For(
        params OriginTlsHostnameAssociationChange[] changes) =>
        new() { Config = changes.ToList() };

    /// <summary>Checks the request before anything is sent.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">
    /// The config array is missing or empty, or an entry names no hostname.
    /// </exception>
    /// <remarks>
    /// An empty <c>config</c> is refused here on purpose: the call would succeed and change nothing, which
    /// reads like a applied change in a log. Cloudflare marks all three fields of an entry optional, so
    /// only the hostname - the one thing that identifies what is being changed - is required locally.
    /// </remarks>
    public void EnsureValid()
    {
        if (Config is not { Count: > 0 } config)
        {
            throw new Exceptions.CloudflareConfigurationException(
                "Changing hostname client authentication needs at least one entry in config.");
        }

        if (config.Any(change => string.IsNullOrWhiteSpace(change.Hostname)))
        {
            throw new Exceptions.CloudflareConfigurationException(
                "Every config entry needs the hostname it changes.");
        }
    }

    /// <summary>Describes the batch.</summary>
    /// <returns>How many changes and what they do.</returns>
    public override string ToString() =>
        Config is { Count: > 0 } config
            ? $"{config.Count} change(s): {string.Join(", ", config.Select(change => change.ToString()))}"
            : "(no changes)";
}

/// <summary>
/// A client certificate uploaded for per-hostname authenticated origin pulls, from
/// <c>/zones/{zone_id}/origin_tls_client_auth/hostnames/certificates</c>.
/// </summary>
/// <remarks>
/// Ten of these are allowed per zone. The private key is not part of this model: it is only ever sent, on
/// upload, and Cloudflare does not return it for a hostname certificate.
/// </remarks>
public sealed class OriginTlsClientCertificate
{
    /// <summary>Certificate identifier, at most 36 characters.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The certificate, in PEM. Never printed by <see cref="ToString"/>.</summary>
    [JsonPropertyName("certificate")]
    public string? Certificate { get; set; }

    /// <summary>When the certificate expires.</summary>
    [JsonPropertyName("expires_on")]
    public DateTimeOffset? ExpiresOn { get; set; }

    /// <summary>Authority that issued it.</summary>
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    /// <summary>Serial number on the uploaded certificate.</summary>
    [JsonPropertyName("serial_number")]
    public string? SerialNumber { get; set; }

    /// <summary>Hash type used for the certificate.</summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>Activation status, one of <see cref="OriginTlsClientAuthStatuses"/>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>When it was uploaded.</summary>
    [JsonPropertyName("uploaded_on")]
    public DateTimeOffset? UploadedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>One line of status: id, activation status and expiry. The PEM is left out.</summary>
    /// <returns>Identifier, status and expiry.</returns>
    public override string ToString() =>
        Id is { Length: > 0 } id
            ? ExpiresOn is { } expires ? $"{id} ({Status ?? "unknown"}, expires {expires:u})" : $"{id} ({Status ?? "unknown"})"
            : Status ?? "(no certificate)";
}

/// <summary>
/// Body of both certificate uploads: <c>POST …/origin_tls_client_auth/hostnames/certificates</c> for a
/// hostname certificate and the deprecated <c>POST …/origin_tls_client_auth</c> for a zone-level one.
/// </summary>
/// <remarks>
/// Both endpoints take exactly these two fields and both require them, so one request type serves both. The
/// private key travels in the body over TLS and is never echoed by <see cref="ToString"/> - it reports
/// lengths instead, the way <c>PagesUploadToken</c> reports its length rather than the token.
/// </remarks>
public sealed class UploadOriginTlsCertificateRequest
{
    /// <summary>The certificate, in PEM. Required.</summary>
    [JsonPropertyName("certificate")]
    public string? Certificate { get; set; }

    /// <summary>The certificate's private key, in PEM. Required.</summary>
    [JsonPropertyName("private_key")]
    public string? PrivateKey { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Builds the body from a certificate and its key.</summary>
    /// <param name="certificate">PEM certificate.</param>
    /// <param name="privateKey">PEM private key matching the certificate.</param>
    /// <returns>The request.</returns>
    public static UploadOriginTlsCertificateRequest Of(string certificate, string privateKey) =>
        new() { Certificate = certificate, PrivateKey = privateKey };

    /// <summary>Checks the request before anything is sent.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">Either half is missing.</exception>
    /// <remarks>
    /// Whether the key actually matches the certificate cannot be checked without parsing both, so that is
    /// left to Cloudflare - the same call <c>CreateCustomHostnameRequest</c> makes about certificate pairs.
    /// </remarks>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Certificate))
        {
            throw new Exceptions.CloudflareConfigurationException(
                "Uploading a client certificate needs the certificate itself.");
        }

        if (string.IsNullOrWhiteSpace(PrivateKey))
        {
            throw new Exceptions.CloudflareConfigurationException(
                "Uploading a client certificate needs its private key.");
        }
    }

    /// <summary>Reports the sizes of both halves and never their contents.</summary>
    /// <returns>Lengths of the certificate and key.</returns>
    public override string ToString() =>
        $"certificate ({Certificate?.Length ?? 0} characters) and private key ({PrivateKey?.Length ?? 0} characters)";
}

/// <summary>
/// The zone-level enablement switch for authenticated origin pulls, from
/// <c>/zones/{zone_id}/origin_tls_client_auth/settings</c>.
/// </summary>
/// <remarks>
/// One boolean and nothing else - no <c>id</c>, no <c>editable</c>, no <c>modified_on</c>, unlike the zone
/// settings under <c>/zones/{zone_id}/settings</c>. This is its own resource, not one of those.
/// </remarks>
public sealed class OriginTlsClientAuthSetting
{
    /// <summary>Whether zone-level authenticated origin pulls are on.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>One line of status.</summary>
    /// <returns>Whether it is on, off, or not reported.</returns>
    public override string ToString() =>
        Enabled switch
        {
            true => "zone-level authenticated origin pulls enabled",
            false => "zone-level authenticated origin pulls disabled",
            _ => "zone-level authenticated origin pulls not reported",
        };
}

/// <summary>Body of <c>PUT /zones/{zone_id}/origin_tls_client_auth/settings</c>.</summary>
/// <remarks>
/// The schema marks <c>enabled</c> required, so it is a plain <see cref="bool"/> here and is always sent -
/// an omitted flag would not be a no-op so much as an invalid request.
/// </remarks>
public sealed class UpdateOriginTlsClientAuthSettingRequest
{
    /// <summary>Whether zone-level authenticated origin pulls should be on.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Builds the body that turns the zone-level switch on.</summary>
    /// <returns>The request.</returns>
    public static UpdateOriginTlsClientAuthSettingRequest Enable() => new() { Enabled = true };

    /// <summary>Builds the body that turns it off.</summary>
    /// <returns>The request.</returns>
    public static UpdateOriginTlsClientAuthSettingRequest Disable() => new() { Enabled = false };

    /// <summary>One line of intent.</summary>
    /// <returns>What the body asks for.</returns>
    public override string ToString() => Enabled ? "enable" : "disable";
}

/// <summary>
/// A zone-level certificate for authenticated origin pulls, from the <b>deprecated</b> top-level
/// <c>/zones/{zone_id}/origin_tls_client_auth</c> endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Cloudflare marks all four of those endpoints deprecated: the current way is one certificate per hostname
/// (<see cref="OriginTlsClientCertificate"/>) plus an association (<see cref="OriginTlsHostnameAssociation"/>).
/// This model is here so an existing zone-level certificate can still be read and removed.
/// </para>
/// <para>
/// <b>This one does carry <see cref="PrivateKey"/>.</b> The deprecated endpoints return the key material they
/// were given, in plaintext, in every response - list, get, upload and delete alike. <see cref="ToString"/>
/// reports its length and nothing else, and anything logging a response from these endpoints should be
/// treated as holding a secret.
/// </para>
/// </remarks>
public sealed class ZoneAuthenticatedOriginPull
{
    /// <summary>Certificate identifier, at most 36 characters.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The zone's leaf certificate, in PEM.</summary>
    [JsonPropertyName("certificate")]
    public string? Certificate { get; set; }

    /// <summary>Whether zone-level authenticated origin pulls are enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>When the certificate expires.</summary>
    [JsonPropertyName("expires_on")]
    public DateTimeOffset? ExpiresOn { get; set; }

    /// <summary>Authority that issued it.</summary>
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    /// <summary>
    /// The zone's private key, in PEM. Returned by the deprecated endpoints; never printed by
    /// <see cref="ToString"/>.
    /// </summary>
    [JsonPropertyName("private_key")]
    public string? PrivateKey { get; set; }

    /// <summary>Hash type used for the certificate.</summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>
    /// Status of the certificate activation. Published as its own enum for this resource, so it is kept as a
    /// string: <see cref="OriginTlsClientAuthStatuses"/> names the values the hostname-level resources use
    /// and an unlisted one is never a deserialisation failure.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>When it was uploaded.</summary>
    [JsonPropertyName("uploaded_on")]
    public DateTimeOffset? UploadedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Whether a private key came back with it.</summary>
    [JsonIgnore]
    public bool HasPrivateKey => PrivateKey is { Length: > 0 };

    /// <summary>
    /// One line of status: id, activation status, expiry and the length of the key - never the key or the
    /// certificate.
    /// </summary>
    /// <returns>Identifier, status, expiry and key length.</returns>
    public override string ToString()
    {
        if (Id is not { Length: > 0 } id)
        {
            return "(no certificate)";
        }

        var expiry = ExpiresOn is { } expires ? $", expires {expires:u}" : string.Empty;
        var key = HasPrivateKey ? $", private key {PrivateKey!.Length} characters" : string.Empty;
        return $"{id} ({Status ?? "unknown"}{expiry}{key})";
    }
}

/// <summary>Activation statuses of an origin TLS client auth certificate or association.</summary>
/// <remarks>
/// Seven values, and note the difference from the custom hostname fallback origin's six: this set includes
/// <c>deleted</c>, which that one does not. They are separate resources with separate enums and are modelled
/// separately rather than sharing a list that would be wrong for one of them.
/// </remarks>
public static class OriginTlsClientAuthStatuses
{
    /// <summary>Setup has begun.</summary>
    public const string Initializing = "initializing";

    /// <summary>Waiting to be deployed to the edge.</summary>
    public const string PendingDeployment = "pending_deployment";

    /// <summary>Deletion has started.</summary>
    public const string PendingDeletion = "pending_deletion";

    /// <summary>Live.</summary>
    public const string Active = "active";

    /// <summary>Removed.</summary>
    public const string Deleted = "deleted";

    /// <summary>Deployment took too long.</summary>
    public const string DeploymentTimedOut = "deployment_timed_out";

    /// <summary>Deletion took too long.</summary>
    public const string DeletionTimedOut = "deletion_timed_out";

    /// <summary>Every status the schema defines, in the order it defines them.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Initializing, PendingDeployment, PendingDeletion, Active, Deleted, DeploymentTimedOut, DeletionTimedOut,
    };
}
