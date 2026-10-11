namespace CFNet;

/// <summary>
/// Central, immutable defaults used across the library.
/// </summary>
public static class CloudflareDefaults
{
    /// <summary>Base address of the Cloudflare REST API (v4).</summary>
    public const string BaseAddress = "https://api.cloudflare.com/client/v4/";

    /// <summary>
    /// Default <c>User-Agent</c> header value. Cloudflare rate limits and debug logs are much
    /// easier to work with when a recognisable client identifier is sent.
    /// </summary>
    public const string UserAgent = "CF.Net/0.1.0 (+https://github.com/ilwren/CF.Net)";

    /// <summary>Name of the configuration section used by <c>AddCloudflare(IConfiguration)</c>.</summary>
    public const string ConfigurationSectionName = "Cloudflare";

    /// <summary>Environment variables consumed by <see cref="Configuration.CloudflareClientOptions.FromEnvironment()"/> and the tests.</summary>
    public static class EnvironmentVariables
    {
        public const string ApiToken = "CLOUDFLARE_API_TOKEN";
        public const string GlobalApiKey = "CLOUDFLARE_GLOBAL_API_KEY";
        public const string ApiEmail = "CLOUDFLARE_API_EMAIL";
        public const string AccountId = "CLOUDFLARE_ACCOUNT_ID";
        public const string ZoneId = "CLOUDFLARE_ZONE_ID";
        public const string BaseAddress = "CLOUDFLARE_BASE_ADDRESS";
    }

    /// <summary>Header names that Cloudflare understands.</summary>
    public static class Headers
    {
        /// <summary>Bearer token authentication (recommended).</summary>
        public const string Authorization = "Authorization";

        /// <summary>Legacy global API key authentication.</summary>
        public const string AuthKey = "X-Auth-Key";

        /// <summary>Account e-mail, required together with <see cref="AuthKey"/>.</summary>
        public const string AuthEmail = "X-Auth-Email";

        /// <summary>Selects the R2 jurisdiction (data residency boundary) for a request.</summary>
        public const string R2Jurisdiction = "cf-r2-jurisdiction";

        /// <summary>Returned by the KV API carrying the JSON metadata of a stored value.</summary>
        public const string KvMetadata = "CF-KV-Metadata";

        /// <summary>Cloudflare request id, useful when opening a support ticket.</summary>
        public const string CfRay = "CF-RAY";
    }
}
