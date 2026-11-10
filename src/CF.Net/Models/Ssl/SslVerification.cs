using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Ssl;

/// <summary>
/// The validation state of one certificate in a zone, from <c>GET /zones/{zone_id}/ssl/verification</c>,
/// which returns an array of these - one entry per certificate being validated.
/// </summary>
/// <remarks>
/// This is the endpoint that answers "why has this certificate not been issued yet": <see cref="VerificationInfo"/>
/// carries the exact DNS record or URL the authority is waiting for, and <see cref="VerificationStatus"/> says
/// whether Cloudflare has found it. Pass <c>retry: true</c> to make Cloudflare re-run the check immediately
/// instead of waiting for its next scheduled attempt.
/// </remarks>
public sealed class SslVerificationDetails
{
    /// <summary>The certificate pack this entry belongs to.</summary>
    [JsonPropertyName("cert_pack_uuid")]
    public string? CertificatePackId { get; set; }

    /// <summary>Current status of the certificate; see <see cref="SslVerificationCertificateStatuses"/>.</summary>
    [JsonPropertyName("certificate_status")]
    public string? CertificateStatus { get; set; }

    /// <summary>Validation method in use for the pack; see <see cref="SslValidationMethods"/>.</summary>
    [JsonPropertyName("validation_method")]
    public string? ValidationMethod { get; set; }

    /// <summary>The record or URL the authority is waiting to see.</summary>
    [JsonPropertyName("verification_info")]
    public SslVerificationInfo? VerificationInfo { get; set; }

    /// <summary>Whether the required verification information was found; omitted when unknown.</summary>
    [JsonPropertyName("verification_status")]
    public bool? VerificationStatus { get; set; }

    /// <summary>How the host was verified: <c>cname</c> or <c>meta tag</c>.</summary>
    [JsonPropertyName("verification_type")]
    public string? VerificationType { get; set; }

    /// <summary>True when the certificate authority is manually reviewing the order.</summary>
    [JsonPropertyName("brand_check")]
    public bool? BrandCheck { get; set; }

    /// <summary>The certificate's signature algorithm, e.g. <c>SHA256WithRSA</c> or <c>ECDSAWithSHA256</c>.</summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True only when Cloudflare explicitly reported the verification information as present.</summary>
    [JsonIgnore]
    public bool IsVerified => VerificationStatus == true;

    /// <inheritdoc />
    public override string ToString() => $"{CertificatePackId ?? "(no pack)"}: {CertificateStatus ?? "unknown"}";
}

/// <summary>What the certificate authority is looking for when it validates a host.</summary>
/// <remarks>
/// The schema enumerates <see cref="RecordName"/> as one of <c>record_name</c>, <c>http_url</c>, <c>cname</c> or
/// <c>txt_name</c>, and <see cref="RecordTarget"/> as one of <c>record_value</c>, <c>http_body</c>,
/// <c>cname_target</c> or <c>txt_value</c>; both are plain hostnames in practice, so they are left as strings
/// rather than being narrowed to a constant set that would reject a value Cloudflare starts returning.
/// </remarks>
public sealed class SslVerificationInfo
{
    /// <summary>Name of the CNAME or TXT record to create, or the URL to serve.</summary>
    [JsonPropertyName("record_name")]
    public string? RecordName { get; set; }

    /// <summary>Value that record has to carry.</summary>
    [JsonPropertyName("record_target")]
    public string? RecordTarget { get; set; }

    /// <summary>Administrative e-mail address, for an e-mail challenge.</summary>
    [JsonPropertyName("policy_email")]
    public string? PolicyEmail { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        RecordName is null ? "(no record)" : $"{RecordName} -> {RecordTarget ?? "(no target)"}";
}

/// <summary>
/// Result of <c>PATCH /zones/{zone_id}/ssl/verification/{certificate_pack_id}</c>, which asks Cloudflare to
/// re-check validation immediately.
/// </summary>
public sealed class SslValidationMethodUpdate
{
    /// <summary>Status of the certificate pack after the re-check, e.g. <c>pending_validation</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>The validation method now in use for the pack.</summary>
    [JsonPropertyName("validation_method")]
    public string? ValidationMethod { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Status ?? "unknown"} via {ValidationMethod ?? "the pack's current method"}";
}

/// <summary>Body of the validation-method <c>PATCH</c>.</summary>
/// <remarks>
/// Sending a method makes Cloudflare switch to it and validate with it straight away; sending nothing
/// (<see cref="RevalidateNow"/>) only asks for an immediate re-check of the method already in use. This is a
/// separate switch from <c>RestartValidationAsync</c> on the certificate pack: that one restarts a pack stuck
/// in <c>validation_timed_out</c>, this one drives the DCV check itself.
/// </remarks>
public sealed class UpdateSslValidationMethodRequest
{
    /// <summary>Desired validation method; see <see cref="SslValidationMethods"/>.</summary>
    [JsonPropertyName("validation_method")]
    public string? ValidationMethod { get; set; }

    /// <summary>Carries any extra field a caller wants to send.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>A body asking for an immediate re-check, without changing the method.</summary>
    public static UpdateSslValidationMethodRequest RevalidateNow() => new();

    /// <summary>A body switching the pack to <paramref name="validationMethod"/> and validating with it.</summary>
    public static UpdateSslValidationMethodRequest WithMethod(string validationMethod) =>
        new() { ValidationMethod = validationMethod };

    /// <summary>True when the body carries nothing, i.e. it is a bare re-check.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(ValidationMethod) && (AdditionalData is null || AdditionalData.Count == 0);

    /// <inheritdoc />
    public override string ToString() => IsEmpty ? "revalidate now" : ValidationMethod!;
}

/// <summary>The <c>certificate_status</c> values a verification entry reports.</summary>
public static class SslVerificationCertificateStatuses
{
    /// <summary>The certificate order is being set up.</summary>
    public const string Initializing = "initializing";

    /// <summary>The authority is authorising the order.</summary>
    public const string Authorizing = "authorizing";

    /// <summary>The certificate is live.</summary>
    public const string Active = "active";

    /// <summary>The certificate has expired.</summary>
    public const string Expired = "expired";

    /// <summary>The certificate is being issued.</summary>
    public const string Issuing = "issuing";

    /// <summary>Validation is about to time out.</summary>
    public const string TimingOut = "timing_out";

    /// <summary>Issued, waiting to reach the edge.</summary>
    public const string PendingDeployment = "pending_deployment";

    /// <summary>Every value the schema lists.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Initializing, Authorizing, Active, Expired, Issuing, TimingOut, PendingDeployment,
    };
}

/// <summary>The <c>verification_type</c> values a verification entry reports.</summary>
public static class SslVerificationTypes
{
    /// <summary>Verified through a CNAME record.</summary>
    public const string Cname = "cname";

    /// <summary>Verified through a meta tag - note the value contains a space.</summary>
    public const string MetaTag = "meta tag";

    /// <summary>Every value the schema lists.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Cname, MetaTag };
}
