using System.Text.Json;
using System.Text.Json.Serialization;
using CFNet.Http;

namespace CFNet.Models.Rules;

/// <summary>
/// A Cloudflare list: a named, account-scoped set of values that rules match against.
/// </summary>
/// <remarks>
/// <para>
/// Returned by <c>GET /accounts/{account_id}/rules/lists</c> and by the create, read and update calls on
/// one list, all of which answer with the same shape. A list's <see cref="Kind"/> decides what its items
/// hold and never changes after creation; a list is referenced by rulesets and filters through its
/// <see cref="Name"/>, which is why <see cref="NumReferencingFilters"/> exists - a list in use cannot be
/// deleted without breaking whatever refers to it.
/// </para>
/// <para>
/// This is the storage half of Bulk Redirects: a <c>redirect</c> list holds the source and target URLs, and
/// a ruleset rule with a <c>from_list</c> action parameter is what turns them into redirects. The list on
/// its own does nothing.
/// </para>
/// </remarks>
public sealed class RulesList
{
    /// <summary>The list's id, which is what the item endpoints address.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// The name rules refer to, e.g. <c>my_redirects</c>. Unique per account, and not changeable later.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Free text. The only field an update may change.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>What the items hold. One of the <see cref="RulesListKind"/> values.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>How many items the list currently holds.</summary>
    [JsonPropertyName("num_items")]
    public int? NumItems { get; set; }

    /// <summary>How many filters and rules currently refer to this list by name.</summary>
    [JsonPropertyName("num_referencing_filters")]
    public int? NumReferencingFilters { get; set; }

    /// <summary>When the list was created.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>When the list or its items last changed.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True when the list holds redirect entries, the Bulk Redirects case.</summary>
    [JsonIgnore]
    public bool IsRedirectList => string.Equals(Kind, RulesListKind.Redirect, StringComparison.Ordinal);

    /// <summary>A short label for logs: name, kind and item count.</summary>
    /// <returns>The list described in one line.</returns>
    public override string ToString() =>
        string.IsNullOrEmpty(Name) ? "(unnamed list)" : $"{Name} ({Kind}, {NumItems} item(s))";
}

/// <summary>The kinds of list Cloudflare supports. A list's kind is fixed when it is created.</summary>
public static class RulesListKind
{
    /// <summary>IP addresses, in CIDR or single-address form.</summary>
    public const string Ip = "ip";

    /// <summary>Source and target URL pairs, the Bulk Redirects case.</summary>
    public const string Redirect = "redirect";

    /// <summary>Hostnames.</summary>
    public const string Hostname = "hostname";

    /// <summary>Autonomous system numbers.</summary>
    public const string Asn = "asn";
}

/// <summary>Creates a list: <c>POST /accounts/{account_id}/rules/lists</c>.</summary>
/// <remarks>
/// Only <see cref="Name"/>, <see cref="Kind"/> and <see cref="Description"/> are accepted - items are added
/// afterwards with the item endpoints, and the kind cannot be changed once the list exists.
/// </remarks>
public sealed class CreateRulesListRequest
{
    /// <summary>The name rules will refer to. Required, and unique within the account.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>What the items will hold. Required. One of the <see cref="RulesListKind"/> values.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>Free text describing the list.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Updates a list: <c>PUT /accounts/{account_id}/rules/lists/{list_id}</c>.
/// </summary>
/// <remarks>
/// The endpoint is a <c>PUT</c>, not a <c>PATCH</c>, and Cloudflare still only lets the description change:
/// the name and kind are fixed at creation. Sending either one is refused by the API rather than applied.
/// </remarks>
public sealed class UpdateRulesListRequest
{
    /// <summary>The new description. Sending nothing clears it.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>One item in a list, as <c>GET …/rules/lists/{list_id}/items</c> returns it.</summary>
/// <remarks>
/// Cloudflare answers with one of four shapes depending on the list's kind, all sharing an id, timestamps
/// and an optional comment, and each carrying exactly one value: <c>ip</c>, <c>hostname</c>, <c>redirect</c>
/// or <c>asn</c>. This model declares all four as optional rather than splitting into four types, so a
/// caller reading a mixed account gets every field the API sent, and <see cref="ValueKind"/> says which one
/// is populated.
/// </remarks>
public sealed class RulesListItem
{
    /// <summary>The item's id, which is what a delete identifies it by.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>IP address, when the list's kind is <see cref="RulesListKind.Ip"/>.</summary>
    [JsonPropertyName("ip")]
    public string? Ip { get; set; }

    /// <summary>Hostname value, when the list's kind is <see cref="RulesListKind.Hostname"/>.</summary>
    [JsonPropertyName("hostname")]
    public RulesListHostname? Hostname { get; set; }

    /// <summary>Redirect entry, when the list's kind is <see cref="RulesListKind.Redirect"/>.</summary>
    [JsonPropertyName("redirect")]
    public RulesListRedirect? Redirect { get; set; }

    /// <summary>Autonomous system number, when the list's kind is <see cref="RulesListKind.Asn"/>.</summary>
    [JsonPropertyName("asn")]
    public int? Asn { get; set; }

    /// <summary>Free text attached to the item.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>When the item was added.</summary>
    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>When the item last changed.</summary>
    [JsonPropertyName("modified_on")]
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// Which value this item holds - <c>ip</c>, <c>hostname</c>, <c>redirect</c> or <c>asn</c> - or
    /// <c>null</c> when none is populated.
    /// </summary>
    [JsonIgnore]
    public string? ValueKind =>
        Ip is not null ? RulesListKind.Ip
        : Hostname is not null ? RulesListKind.Hostname
        : Redirect is not null ? RulesListKind.Redirect
        : Asn is not null ? RulesListKind.Asn
        : null;

    /// <summary>The item's value as text, whatever kind it is. Useful in logs and assertions.</summary>
    /// <returns>The value, or <c>(empty)</c> when the item carries none.</returns>
    public override string ToString() => ValueKind switch
    {
        RulesListKind.Ip => Ip!,
        // The hostname's own label, which is the one that says when the exact name is excluded.
        RulesListKind.Hostname => Hostname!.ToString(),
        RulesListKind.Redirect => Redirect!.ToString(),
        RulesListKind.Asn => Asn!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "(empty)",
    };
}

/// <summary>A hostname value inside a hostname list.</summary>
public sealed class RulesListHostname
{
    /// <summary>The hostname itself, e.g. <c>example.com</c>.</summary>
    [JsonPropertyName("url_hostname")]
    public string? UrlHostname { get; set; }

    /// <summary>
    /// True to match subdomains of <see cref="UrlHostname"/> but not the hostname itself.
    /// </summary>
    [JsonPropertyName("exclude_exact_hostname")]
    public bool? ExcludeExactHostname { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The hostname, with a marker when the exact name is excluded.</summary>
    /// <returns>The hostname as text.</returns>
    public override string ToString() =>
        ExcludeExactHostname is true ? $"*.{UrlHostname} (not {UrlHostname})" : UrlHostname ?? string.Empty;
}

/// <summary>One Bulk Redirects entry: where a source URL should send traffic.</summary>
/// <remarks>
/// <para>
/// Both URLs are matched and redirected by the ruleset rule that refers to the list, not by the list
/// itself. The flags decide how much of the original request survives the redirect:
/// <see cref="PreserveQueryString"/>, <see cref="PreservePathSuffix"/> and <see cref="IncludeSubdomains"/>
/// are the ones the dashboard exposes as toggles, and <see cref="SubpathMatching"/> is the one that lets a
/// single entry cover everything below a path.
/// </para>
/// <para>
/// Every flag is optional and Cloudflare defaults them per plan, so this model keeps them nullable and the
/// serializer omits the ones left unset rather than sending <c>false</c> and overriding a default.
/// </para>
/// </remarks>
public sealed class RulesListRedirect
{
    /// <summary>The URL to redirect from, e.g. <c>example.com/old</c>.</summary>
    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }

    /// <summary>The URL to redirect to.</summary>
    [JsonPropertyName("target_url")]
    public string? TargetUrl { get; set; }

    /// <summary>
    /// The HTTP status to answer with: 301, 302, 307 or 308. 301 and 302 are the common pair; 307 and 308
    /// keep the request method.
    /// </summary>
    [JsonPropertyName("status_code")]
    public int? StatusCode { get; set; }

    /// <summary>True to apply the entry to subdomains of <see cref="SourceUrl"/>'s hostname as well.</summary>
    [JsonPropertyName("include_subdomains")]
    public bool? IncludeSubdomains { get; set; }

    /// <summary>True to keep the original query string on the target.</summary>
    [JsonPropertyName("preserve_query_string")]
    public bool? PreserveQueryString { get; set; }

    /// <summary>True to keep whatever path followed the matched part of the source.</summary>
    [JsonPropertyName("preserve_path_suffix")]
    public bool? PreservePathSuffix { get; set; }

    /// <summary>True to match the source as a path prefix rather than exactly.</summary>
    [JsonPropertyName("subpath_matching")]
    public bool? SubpathMatching { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Source, status and target on one line.</summary>
    /// <returns>The redirect as text.</returns>
    public override string ToString() =>
        StatusCode is { } code
            ? $"{SourceUrl} -{code}-> {TargetUrl}"
            : $"{SourceUrl} -> {TargetUrl}";
}

/// <summary>An item to add to a list, or to replace a list's items with.</summary>
/// <remarks>
/// Exactly one of <see cref="Ip"/>, <see cref="Hostname"/>, <see cref="Redirect"/> and <see cref="Asn"/>
/// should be set, and it has to be the one the list's kind expects - Cloudflare refuses a mismatch. The
/// static helpers build the common cases without an object initializer.
/// </remarks>
public sealed class CreateRulesListItemRequest
{
    /// <summary>An IP address or CIDR block, for an <see cref="RulesListKind.Ip"/> list.</summary>
    [JsonPropertyName("ip")]
    public string? Ip { get; set; }

    /// <summary>A hostname value, for a <see cref="RulesListKind.Hostname"/> list.</summary>
    [JsonPropertyName("hostname")]
    public RulesListHostname? Hostname { get; set; }

    /// <summary>A redirect entry, for a <see cref="RulesListKind.Redirect"/> list.</summary>
    [JsonPropertyName("redirect")]
    public RulesListRedirect? Redirect { get; set; }

    /// <summary>An autonomous system number, for an <see cref="RulesListKind.Asn"/> list.</summary>
    [JsonPropertyName("asn")]
    public int? Asn { get; set; }

    /// <summary>Free text attached to the item.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Builds an entry for an IP list.</summary>
    /// <param name="ip">The address or CIDR block.</param>
    /// <param name="comment">Optional comment.</param>
    /// <returns>A request holding one IP item.</returns>
    public static CreateRulesListItemRequest ForIp(string ip, string? comment = null) =>
        new() { Ip = ip, Comment = comment };

    /// <summary>Builds an entry for a redirect list.</summary>
    /// <param name="sourceUrl">The URL to redirect from.</param>
    /// <param name="targetUrl">The URL to redirect to.</param>
    /// <param name="statusCode">301, 302, 307 or 308; <c>null</c> leaves Cloudflare's default.</param>
    /// <param name="comment">Optional comment.</param>
    /// <returns>A request holding one redirect item.</returns>
    public static CreateRulesListItemRequest ForRedirect(
        string sourceUrl,
        string targetUrl,
        int? statusCode = null,
        string? comment = null) =>
        new()
        {
            Redirect = new RulesListRedirect
            {
                SourceUrl = sourceUrl,
                TargetUrl = targetUrl,
                StatusCode = statusCode,
            },
            Comment = comment,
        };

    /// <summary>Builds an entry for a hostname list.</summary>
    /// <param name="urlHostname">The hostname.</param>
    /// <param name="excludeExactHostname">True to match only subdomains of it.</param>
    /// <param name="comment">Optional comment.</param>
    /// <returns>A request holding one hostname item.</returns>
    public static CreateRulesListItemRequest ForHostname(
        string urlHostname,
        bool? excludeExactHostname = null,
        string? comment = null) =>
        new()
        {
            Hostname = new RulesListHostname
            {
                UrlHostname = urlHostname,
                ExcludeExactHostname = excludeExactHostname,
            },
            Comment = comment,
        };

    /// <summary>Builds an entry for an ASN list.</summary>
    /// <param name="asn">The autonomous system number.</param>
    /// <param name="comment">Optional comment.</param>
    /// <returns>A request holding one ASN item.</returns>
    public static CreateRulesListItemRequest ForAsn(int asn, string? comment = null) =>
        new() { Asn = asn, Comment = comment };

    /// <summary>Which value this request carries, or <c>null</c> when it carries none.</summary>
    [JsonIgnore]
    public string? ValueKind =>
        Ip is not null ? RulesListKind.Ip
        : Hostname is not null ? RulesListKind.Hostname
        : Redirect is not null ? RulesListKind.Redirect
        : Asn is not null ? RulesListKind.Asn
        : null;
}

/// <summary>
/// The receipt Cloudflare answers an item change with: <c>{"operation_id": "…"}</c>.
/// </summary>
/// <remarks>
/// Adding, replacing and deleting items are all asynchronous - the call is accepted, the work happens in
/// the background, and the operation id is how you ask whether it finished. Nothing about the items
/// themselves comes back on this response.
/// </remarks>
public sealed class RulesListItemOperation
{
    /// <summary>
    /// The bulk operation id, which <c>GET …/rules/lists/bulk_operations/{operation_id}</c> reports on.
    /// </summary>
    [JsonPropertyName("operation_id")]
    public string? OperationId { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The operation id.</summary>
    /// <returns>The id, or an empty string when Cloudflare sent none.</returns>
    public override string ToString() => OperationId ?? string.Empty;
}

/// <summary>
/// The state of a bulk item operation: <c>GET /accounts/{account_id}/rules/lists/bulk_operations/{operation_id}</c>.
/// </summary>
/// <remarks>
/// Cloudflare answers with one of three shapes - pending or running (<c>id</c> and <c>status</c> only),
/// completed (adds <c>completed</c>), or failed (adds <c>completed</c> and <c>error</c>). They are declared
/// as one model with optional fields, because the difference is which of them are present.
/// </remarks>
public sealed class RulesListBulkOperation
{
    /// <summary>The operation id, echoing the one asked about.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>One of the <see cref="RulesListBulkOperationStatus"/> values.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>When the operation finished. Absent while it is still pending or running.</summary>
    [JsonPropertyName("completed")]
    public DateTimeOffset? Completed { get; set; }

    /// <summary>Why it failed. Only present on a failed operation.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>True once the operation has stopped, whether it succeeded or failed.</summary>
    [JsonIgnore]
    public bool IsFinished =>
        string.Equals(Status, RulesListBulkOperationStatus.Completed, StringComparison.Ordinal)
        || string.Equals(Status, RulesListBulkOperationStatus.Failed, StringComparison.Ordinal);

    /// <summary>True when the operation stopped because it failed; <see cref="Error"/> says why.</summary>
    [JsonIgnore]
    public bool HasFailed => string.Equals(Status, RulesListBulkOperationStatus.Failed, StringComparison.Ordinal);

    /// <summary>Id, status and - when it failed - the error.</summary>
    /// <returns>The operation described in one line.</returns>
    public override string ToString() =>
        HasFailed ? $"{Id}: failed ({Error})" : $"{Id}: {Status}";
}

/// <summary>The statuses a bulk item operation reports.</summary>
public static class RulesListBulkOperationStatus
{
    /// <summary>Accepted, not started.</summary>
    public const string Pending = "pending";

    /// <summary>Being applied.</summary>
    public const string Running = "running";

    /// <summary>Applied.</summary>
    public const string Completed = "completed";

    /// <summary>Stopped with an error.</summary>
    public const string Failed = "failed";
}

/// <summary>
/// Filters for listing a list's items: <c>GET …/rules/lists/{list_id}/items</c>.
/// </summary>
/// <remarks>
/// This endpoint pages by cursor, not by page number, and the cursor it accepts is <c>after</c> - the value
/// the previous response reported in <c>result_info.cursors.after</c>. The response's cursors also carry a
/// <c>before</c>, which walks backwards, but the endpoint takes only <c>after</c>, so that is all this model
/// sends. <see cref="CloudflarePage{T}.NextCursor"/> is the usual way to keep going.
/// </remarks>
public sealed class RulesListItemListParameters : ICloudflareQueryParameters
{
    /// <summary>Cursor to read the next batch from.</summary>
    public string? After { get; set; }

    /// <summary>How many items to return.</summary>
    public int? PerPage { get; set; }

    /// <summary>Only items whose value contains this phrase.</summary>
    public string? Search { get; set; }

    /// <inheritdoc />
    public void Apply(QueryStringBuilder query)
    {
        query.AddIfNotEmpty("after", After);
        query.Add("per_page", PerPage);
        query.AddIfNotEmpty("search", Search);
    }
}

/// <summary>What deleting a list answers with: <c>{"id": "…"}</c>, echoing the list that is gone.</summary>
public sealed class RulesListDeleteResult
{
    /// <summary>The id of the list that was deleted.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The deleted list's id.</summary>
    /// <returns>The id, or an empty string when Cloudflare sent none.</returns>
    public override string ToString() => Id ?? string.Empty;
}

/// <summary>
/// The body a bulk item delete sends: <c>{"items": [{"id": "…"}]}</c>.
/// </summary>
/// <remarks>
/// Unlike adding or replacing items, which send a bare JSON array, deleting identifies the items by id
/// inside an object. Items are removed by id rather than by value, because a value can appear more than
/// once in a list and an id cannot.
/// </remarks>
public sealed class DeleteRulesListItemsRequest
{
    /// <summary>The items to remove.</summary>
    [JsonPropertyName("items")]
    public List<RulesListItemReference>? Items { get; set; }
}

/// <summary>An item referred to by id, which is all a bulk delete needs.</summary>
public sealed class RulesListItemReference
{
    /// <summary>The item's id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Builds a reference to one item.</summary>
    /// <param name="id">The item's id.</param>
    /// <returns>A reference holding that id.</returns>
    public static implicit operator RulesListItemReference(string id) => new() { Id = id };

    /// <summary>The item's id.</summary>
    /// <returns>The id, or an empty string when there is none.</returns>
    public override string ToString() => Id ?? string.Empty;
}
