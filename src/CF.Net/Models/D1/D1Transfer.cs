using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.D1;

/// <summary>
/// Body of <c>POST /accounts/{account_id}/d1/database/{database_id}/export</c>.
/// </summary>
/// <remarks>
/// An export is a polled operation: the first call starts it and every later call has to carry the
/// <c>at_bookmark</c> the previous response returned. Cloudflare cancels an export that stops being
/// polled, which is what <see cref="Services.ID1DatabaseService.ExportAndWaitAsync"/> keeps doing for you.
/// </remarks>
public sealed class D1ExportRequest
{
    /// <summary>Creates the request that starts an export.</summary>
    /// <param name="dumpOptions">Optional restrictions on what the dump contains.</param>
    public static D1ExportRequest Start(D1DumpOptions? dumpOptions = null) =>
        new() { OutputFormat = D1TransferOutputFormats.Polling, DumpOptions = dumpOptions };

    /// <summary>Creates the request that continues polling a running export.</summary>
    /// <param name="currentBookmark">The <c>at_bookmark</c> of the previous response.</param>
    /// <param name="dumpOptions">The options the export was started with.</param>
    public static D1ExportRequest Continue(string currentBookmark, D1DumpOptions? dumpOptions = null) =>
        new()
        {
            OutputFormat = D1TransferOutputFormats.Polling,
            CurrentBookmark = currentBookmark,
            DumpOptions = dumpOptions,
        };

    /// <summary>
    /// How the export is delivered. <c>polling</c> is the only value the schema defines, and it is
    /// required.
    /// </summary>
    [JsonPropertyName("output_format")]
    public string? OutputFormat { get; set; }

    /// <summary>The bookmark of the export being polled; omitted on the call that starts it.</summary>
    [JsonPropertyName("current_bookmark")]
    public string? CurrentBookmark { get; set; }

    /// <summary>What to leave out of the dump, and which tables to restrict it to.</summary>
    [JsonPropertyName("dump_options")]
    public D1DumpOptions? DumpOptions { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Checks the request against what Cloudflare accepts.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">
    /// <see cref="OutputFormat"/> is missing or is not <c>polling</c>.
    /// </exception>
    public void EnsureValid()
    {
        if (!string.Equals(OutputFormat, D1TransferOutputFormats.Polling, StringComparison.OrdinalIgnoreCase))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"D1 export needs output_format '{D1TransferOutputFormats.Polling}', which is the only value Cloudflare defines.");
        }
    }
}

/// <summary>The <c>dump_options</c> of a D1 export.</summary>
public sealed class D1DumpOptions
{
    /// <summary>Export only the table definitions, not their contents.</summary>
    [JsonPropertyName("no_data")]
    public bool? NoData { get; set; }

    /// <summary>Export only each table's contents, not its definition.</summary>
    [JsonPropertyName("no_schema")]
    public bool? NoSchema { get; set; }

    /// <summary>
    /// Restrict the export to these tables. An empty list means the same as omitting it: every table.
    /// </summary>
    [JsonPropertyName("tables")]
    public List<string>? Tables { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>
/// The <c>result</c> of a D1 export call, which reports progress and - once it is complete - where to
/// download the dump.
/// </summary>
public sealed class D1ExportResult
{
    /// <summary>
    /// The time-travel bookmark of this export. Send it back as <c>current_bookmark</c> to keep polling;
    /// it does not change while the export runs.
    /// </summary>
    [JsonPropertyName("at_bookmark")]
    public string? AtBookmark { get; set; }

    /// <summary>Only present when <see cref="Status"/> is <c>error</c>: what went wrong.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Log lines produced since the previous poll.</summary>
    [JsonPropertyName("messages")]
    public List<string>? Messages { get; set; }

    /// <summary>Only present when <see cref="Status"/> is <c>complete</c>: the dump to download.</summary>
    [JsonPropertyName("result")]
    public D1ExportFile? Result { get; set; }

    /// <summary>One of <see cref="D1TransferStatuses"/>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Whether Cloudflare considers this step of the export successful.</summary>
    [JsonPropertyName("success")]
    public bool? Success { get; set; }

    /// <summary>Always <c>export</c> for this endpoint.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The dump finished and <see cref="Result"/> says where to fetch it.</summary>
    [JsonIgnore]
    public bool IsComplete => IsStatus(D1TransferStatuses.Complete);

    /// <summary>The export failed; <see cref="Error"/> carries the reason.</summary>
    [JsonIgnore]
    public bool IsFailed => IsStatus(D1TransferStatuses.Error);

    /// <summary>The export is still running and has to be polled again.</summary>
    [JsonIgnore]
    public bool IsRunning => !IsComplete && !IsFailed;

    /// <summary>The signed download URL, or <c>null</c> until the export is complete.</summary>
    [JsonIgnore]
    public string? SignedUrl => Result?.SignedUrl;

    /// <inheritdoc />
    public override string ToString() => $"{Status ?? "?"} {Result?.Filename ?? SignedUrl ?? string.Empty}".TrimEnd();

    private bool IsStatus(string status) => string.Equals(Status, status, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The completed dump a D1 export points at.</summary>
public sealed class D1ExportFile
{
    /// <summary>Name Cloudflare gave the dump.</summary>
    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    /// <summary>
    /// Temporary URL to download the SQL from. Fetch it with an ordinary HTTP client - it needs no
    /// Cloudflare authentication, and this library's transport would only get in the way.
    /// </summary>
    [JsonPropertyName("signed_url")]
    public string? SignedUrl { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <inheritdoc />
    public override string ToString() => Filename ?? SignedUrl ?? string.Empty;
}

/// <summary>
/// Body of <c>POST /accounts/{account_id}/d1/database/{database_id}/import</c>.
/// </summary>
/// <remarks>
/// One endpoint, three actions, used in this order:
/// <list type="number">
/// <item><description>
/// <see cref="Init"/> - <c>{"action":"init","etag":"…"}</c>. Cloudflare answers with a
/// <see cref="D1ImportResult.Filename"/> and the presigned <see cref="D1ImportResult.UploadUrl"/> to PUT
/// the SQL file to. The etag is an MD5 of the file, which is how Cloudflare notices you have already
/// uploaded it; <see cref="ComputeEtag(byte[])"/> produces it.
/// </description></item>
/// <item><description>
/// <see cref="Ingest"/> - <c>{"action":"ingest","etag":"…","filename":"…"}</c>, once your upload has
/// finished. This starts the import, which blocks the database for its duration.
/// </description></item>
/// <item><description>
/// <see cref="Poll"/> - <c>{"action":"poll","current_bookmark":"…"}</c>, repeated until
/// <see cref="D1ImportResult.IsComplete"/> or <see cref="D1ImportResult.IsFailed"/>.
/// </description></item>
/// </list>
/// The upload itself is a plain PUT to the presigned R2 URL and deliberately not part of this library:
/// it is not a Cloudflare API call, it needs no token, and sending one would only risk the request being
/// rejected for carrying both a signature and an <c>Authorization</c> header.
/// </remarks>
public sealed class D1ImportRequest
{
    /// <summary>Starts an import by asking for an upload URL.</summary>
    /// <param name="etag">MD5 hash of the SQL file, as produced by <see cref="ComputeEtag(byte[])"/>.</param>
    public static D1ImportRequest Init(string etag) =>
        new() { Action = D1ImportActions.Init, Etag = etag };

    /// <summary>Tells D1 to import the file that was uploaded.</summary>
    /// <param name="etag">The same MD5 hash that was sent to <see cref="Init"/>.</param>
    /// <param name="filename">The filename the init response returned.</param>
    public static D1ImportRequest Ingest(string etag, string filename) =>
        new() { Action = D1ImportActions.Ingest, Etag = etag, Filename = filename };

    /// <summary>Asks how a running import is getting on.</summary>
    /// <param name="currentBookmark">The <c>at_bookmark</c> of the previous response.</param>
    public static D1ImportRequest Poll(string currentBookmark) =>
        new() { Action = D1ImportActions.Poll, CurrentBookmark = currentBookmark };

    /// <summary>Which of the three actions this request performs.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>MD5 hash of the SQL file. Required by <c>init</c> and <c>ingest</c>.</summary>
    [JsonPropertyName("etag")]
    public string? Etag { get; set; }

    /// <summary>The filename that was uploaded successfully. Required by <c>ingest</c>.</summary>
    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    /// <summary>Identifies the running import. Required by <c>poll</c>.</summary>
    [JsonPropertyName("current_bookmark")]
    public string? CurrentBookmark { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// Computes the etag Cloudflare asks for: the MD5 hash of the file, lowercase hexadecimal.
    /// </summary>
    /// <param name="contents">The SQL file exactly as it will be uploaded.</param>
    /// <returns>The hash as 32 lowercase hexadecimal characters.</returns>
    /// <remarks>
    /// MD5 here is a content fingerprint Cloudflare uses to recognise a file it already holds - not a
    /// security decision, and not one this library gets to make: the API requires this algorithm.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="contents"/> is null.</exception>
    public static string ComputeEtag(byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

#pragma warning disable CA5351 // Do Not Use Broken Cryptographic Algorithms - Cloudflare's API specifies MD5 for this etag.
        var hash = MD5.HashData(contents);
#pragma warning restore CA5351

        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>Computes the etag of a SQL dump held in a string, encoded as UTF-8.</summary>
    /// <param name="sql">The SQL to upload.</param>
    /// <returns>The hash as 32 lowercase hexadecimal characters.</returns>
    public static string ComputeEtag(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        return ComputeEtag(Encoding.UTF8.GetBytes(sql));
    }

    /// <summary>Checks the request against what each action requires.</summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">
    /// The action is unknown, or a field that action requires is missing.
    /// </exception>
    public void EnsureValid()
    {
        if (string.Equals(Action, D1ImportActions.Init, StringComparison.OrdinalIgnoreCase))
        {
            RequireField(Etag, nameof(Etag), "init");
            return;
        }

        if (string.Equals(Action, D1ImportActions.Ingest, StringComparison.OrdinalIgnoreCase))
        {
            RequireField(Etag, nameof(Etag), "ingest");
            RequireField(Filename, nameof(Filename), "ingest");
            return;
        }

        if (string.Equals(Action, D1ImportActions.Poll, StringComparison.OrdinalIgnoreCase))
        {
            RequireField(CurrentBookmark, nameof(CurrentBookmark), "poll");
            return;
        }

        throw new Exceptions.CloudflareConfigurationException(
            $"D1 import needs action '{D1ImportActions.Init}', '{D1ImportActions.Ingest}' or '{D1ImportActions.Poll}', not '{Action}'.");
    }

    private static void RequireField(string? value, string fieldName, string action)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new Exceptions.CloudflareConfigurationException(
                $"A D1 import with action '{action}' needs {fieldName}.");
        }
    }
}

/// <summary>The <c>result</c> of a D1 import call.</summary>
public sealed class D1ImportResult
{
    /// <summary>
    /// The time-travel bookmark of this import, returned while one is running or has just finished. Send
    /// it back as <c>current_bookmark</c> to keep polling.
    /// </summary>
    [JsonPropertyName("at_bookmark")]
    public string? AtBookmark { get; set; }

    /// <summary>Only present when <see cref="Status"/> is <c>error</c>: why the import failed.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Derived from the database id and the etag, so an upload that already happened is not repeated.
    /// Only returned for the <c>init</c> action.
    /// </summary>
    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    /// <summary>Log lines produced since the previous poll.</summary>
    [JsonPropertyName("messages")]
    public List<string>? Messages { get; set; }

    /// <summary>Only present when <see cref="Status"/> is <c>complete</c>.</summary>
    [JsonPropertyName("result")]
    public D1ImportSummary? Result { get; set; }

    /// <summary>One of <see cref="D1TransferStatuses"/>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Whether Cloudflare considers this step of the import successful.</summary>
    [JsonPropertyName("success")]
    public bool? Success { get; set; }

    /// <summary>Always <c>import</c> for this endpoint.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// The presigned R2 URL to PUT the SQL file to. Only returned for the <c>init</c> action, and only
    /// usable with an ordinary HTTP client - it carries its own signature.
    /// </summary>
    [JsonPropertyName("upload_url")]
    public string? UploadUrl { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The import finished; <see cref="Result"/> says how much it did.</summary>
    [JsonIgnore]
    public bool IsComplete => IsStatus(D1TransferStatuses.Complete);

    /// <summary>The import failed; <see cref="Error"/> carries the reason.</summary>
    [JsonIgnore]
    public bool IsFailed => IsStatus(D1TransferStatuses.Error);

    /// <summary>The import is still running and has to be polled again.</summary>
    [JsonIgnore]
    public bool IsRunning => !IsComplete && !IsFailed;

    /// <inheritdoc />
    public override string ToString() => $"{Status ?? "?"} {Filename ?? string.Empty}".TrimEnd();

    private bool IsStatus(string status) => string.Equals(Status, status, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a completed D1 import did.</summary>
public sealed class D1ImportSummary
{
    /// <summary>
    /// The time-travel bookmark to restore to if you want the database exactly as it was just after the
    /// import succeeded.
    /// </summary>
    [JsonPropertyName("final_bookmark")]
    public string? FinalBookmark { get; set; }

    /// <summary>Statistics of the import as Cloudflare reported them.</summary>
    [JsonPropertyName("meta")]
    public D1ImportMeta? Meta { get; set; }

    /// <summary>The total number of queries that were executed during the import.</summary>
    [JsonPropertyName("num_queries")]
    public double? NumQueries { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The <c>meta</c> of a completed D1 import.</summary>
public sealed class D1ImportMeta
{
    /// <summary>The location that served the import.</summary>
    [JsonPropertyName("served_by")]
    public string? ServedBy { get; set; }

    /// <summary>How long the import took, in milliseconds.</summary>
    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    /// <summary>
    /// Captures the rest of what Cloudflare reports - size after the import, rows read and written, the
    /// last row id - without this library guessing at field names it has not seen documented.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The <c>output_format</c> values a D1 export accepts.</summary>
public static class D1TransferOutputFormats
{
    /// <summary>You will poll the endpoint until the export completes. The only value defined.</summary>
    public const string Polling = "polling";
}

/// <summary>The <c>action</c> values a D1 import accepts, in the order they are used.</summary>
public static class D1ImportActions
{
    /// <summary>You have a new SQL file to upload; asks for the presigned upload URL.</summary>
    public const string Init = "init";

    /// <summary>The file is uploaded; start importing it.</summary>
    public const string Ingest = "ingest";

    /// <summary>Check how the running import is getting on.</summary>
    public const string Poll = "poll";
}

/// <summary>The <c>status</c> of a running or finished D1 export or import.</summary>
public static class D1TransferStatuses
{
    /// <summary>
    /// Still working. The REST schema names only <see cref="Complete"/> and <see cref="Error"/>, but an
    /// in-progress poll reports this, so it is treated as "keep polling" rather than as unknown.
    /// </summary>
    public const string Active = "active";

    /// <summary>Finished successfully; the result member is populated.</summary>
    public const string Complete = "complete";

    /// <summary>Failed; the error member explains why.</summary>
    public const string Error = "error";
}
