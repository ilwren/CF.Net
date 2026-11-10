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

    /// <summary>Environment variables consumed by <c>CloudflareClientOptions.FromEnvironment()</c> and the tests.</summary>
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

        /// <summary>
        /// Carries the new default storage class of <c>PATCH /accounts/{account_id}/r2/buckets/{bucket_name}</c>,
        /// which takes no request body.
        /// </summary>
        public const string R2StorageClass = "cf-r2-storage-class";

        /// <summary>Returned by the KV API carrying the JSON metadata of a stored value.</summary>
        public const string KvMetadata = "CF-KV-Metadata";

        /// <summary>Cloudflare request id, useful when opening a support ticket.</summary>
        public const string CfRay = "CF-RAY";
    }

    /// <summary>
    /// The prefixes Cloudflare puts on the credentials it issues, which is how secret scanners - and
    /// this library - tell one kind of credential from another.
    /// </summary>
    /// <remarks>
    /// Credentials created since 2026 carry one of these prefixes followed by 40 characters and a
    /// checksum. Earlier credentials are unprefixed: a 40-character alphanumeric API token, or a 37-45
    /// character lowercase hexadecimal Global API Key. Nothing here can therefore identify an older
    /// credential, and a prefix is a hint rather than a guarantee.
    /// </remarks>
    public static class TokenPrefixes
    {
        /// <summary>
        /// A user-owned API token. It acts as the user who created it, and is verified by
        /// <c>GET /user/tokens/verify</c>.
        /// </summary>
        public const string UserApiToken = "cfut_";

        /// <summary>
        /// An account-owned API token: a service principal that outlives the person who created it,
        /// which is why it is the better choice for CI. It is verified by
        /// <c>GET /accounts/{account_id}/tokens/verify</c>, and the user-scoped endpoints -
        /// <c>GET /user</c> and <c>GET /user/tokens/verify</c> - reject it.
        /// </summary>
        public const string AccountApiToken = "cfat_";

        /// <summary>The Global API Key: account-wide, cannot be scoped, and needs the account e-mail.</summary>
        public const string GlobalApiKey = "cfk_";
    }

    /// <summary>R2 endpoints that are not part of the v4 REST API but belong to the same account.</summary>
    public static class R2
    {
        /// <summary>Domain that hosts every account's S3-compatible endpoint.</summary>
        public const string S3HostSuffix = "r2.cloudflarestorage.com";

        /// <summary>
        /// Builds the S3-compatible endpoint of an account: <c>https://{account_id}.r2.cloudflarestorage.com</c>.
        /// </summary>
        /// <param name="accountId">The account id.</param>
        /// <returns>The absolute endpoint URL, with no trailing slash.</returns>
        /// <remarks>
        /// R2 object data lives behind this endpoint, and so does everything the v4 API does not cover -
        /// bucket CORS rules (<c>PutBucketCors</c> / <c>GetBucketCors</c>) in particular, which Cloudflare
        /// only exposes through the S3 API. It authenticates with AWS Signature Version 4 using the R2
        /// access key id and secret from the dashboard, not with a Cloudflare API token, so this library
        /// builds the URL but does not sign requests for it: point an S3 SDK or the AWS CLI at it.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="accountId"/> is null or whitespace.</exception>
        public static string S3Endpoint(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                throw new ArgumentException("An account id is needed to build the R2 S3 endpoint.", nameof(accountId));
            }

            return $"https://{accountId.Trim()}.{S3HostSuffix}";
        }
    }
}
