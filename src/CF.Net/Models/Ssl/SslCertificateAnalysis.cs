using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Ssl;

/// <summary>Body of <c>POST /zones/{zone_id}/ssl/analyze</c>, which inspects a certificate.</summary>
/// <remarks>
/// Both fields are optional in the schema. In practice the call is for checking a PEM before uploading it:
/// Cloudflare answers with the hostnames the certificate covers, its signature algorithm and when it expires.
/// <see cref="EnsureValid"/> checks the bundle method against its closed set of values, and refuses a
/// certificate that was set to something blank, since that is a mistake rather than a request.
/// </remarks>
public sealed class AnalyzeSslCertificateRequest
{
    /// <summary>The zone's SSL certificate, or the certificate together with its intermediates, in PEM.</summary>
    [JsonPropertyName("certificate")]
    public string? Certificate { get; set; }

    /// <summary>How to build the chain; see <see cref="SslBundleMethods"/>.</summary>
    [JsonPropertyName("bundle_method")]
    public string? BundleMethod { get; set; }

    /// <summary>Carries any extra field a caller wants to send.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>A request analysing <paramref name="certificate"/>, optionally with a bundle method.</summary>
    public static AnalyzeSslCertificateRequest ForCertificate(string certificate, string? bundleMethod = null) =>
        new() { Certificate = certificate, BundleMethod = bundleMethod };

    /// <summary>Throws when Cloudflare would refuse the body.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">
    /// The bundle method is not one Cloudflare accepts, or the certificate was set but is blank.
    /// </exception>
    public void EnsureValid()
    {
        if (Certificate is not null && string.IsNullOrWhiteSpace(Certificate))
        {
            throw new Exceptions.CloudflareConfigurationException(
                "'certificate' was set but is empty: send the PEM, or leave the property null.");
        }

        if (BundleMethod is not null &&
            !SslBundleMethods.All.Contains(BundleMethod, StringComparer.OrdinalIgnoreCase))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"Bundle method '{BundleMethod}' is not one of {string.Join(", ", SslBundleMethods.All)}.");
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        Certificate is null ? "(no certificate)" : $"certificate, {BundleMethod ?? "default"} bundle";
}
