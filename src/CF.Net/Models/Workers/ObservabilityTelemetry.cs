using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Models.Workers;

/// <summary>
/// An attribute key that has appeared in an account's Worker telemetry.
/// </summary>
/// <remarks>
/// Answered by <c>POST /accounts/{account_id}/workers/observability/telemetry/keys</c>, which is a list that
/// is posted rather than got, because its filters and search terms are a body and not a query string. What a
/// key is called and what type it carries is what a query is built from, so this is the endpoint to ask
/// before writing one.
/// </remarks>
public sealed class ObservabilityTelemetryKey
{
    /// <summary>The key's name, as a filter or a query would spell it.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>When the key was last seen, in milliseconds since the epoch.</summary>
    [JsonPropertyName("lastSeenAt")]
    public long? LastSeenAt { get; set; }

    /// <summary>
    /// When the key was last seen, as a date. <see cref="LastSeenAt"/> counts milliseconds - the published
    /// schema does not say so, and a live answer of <c>1793943698985</c> for a key seen that day is what
    /// settled it - so the same unit is what <c>from</c> and <c>to</c> want.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? LastSeen =>
        LastSeenAt is { } at ? DateTimeOffset.UnixEpoch.AddMilliseconds(at) : null;

    /// <summary>What the key's values are: one of <c>ObservabilityTelemetryValueTypes</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Key and type, on one line.</summary>
    /// <returns>The key described in one line.</returns>
    public override string ToString() => $"{Key} ({Type})";
}

/// <summary>
/// One value an attribute key has been seen with.
/// </summary>
/// <remarks>
/// Answered by <c>POST /accounts/{account_id}/workers/observability/telemetry/values</c>. The value itself is
/// a string, a number or a boolean and the schema does not narrow it further, so it arrives as raw JSON with
/// <see cref="ValueText"/> as the readable form of whichever it turned out to be.
/// </remarks>
public sealed class ObservabilityTelemetryValue
{
    /// <summary>The dataset the value was seen in.</summary>
    [JsonPropertyName("dataset")]
    public string? Dataset { get; set; }

    /// <summary>The key this is a value of.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>What the value is: one of <c>ObservabilityTelemetryValueTypes</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>The value itself, as a string, a number or a boolean.</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// The value as text, whichever kind of it Cloudflare sent: a string unquoted, a number or a boolean as
    /// they are written.
    /// </summary>
    [JsonIgnore]
    public string? ValueText
    {
        get
        {
            if (Value is not { } value)
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }
    }

    /// <summary>Key, value and the dataset it came from, on one line.</summary>
    /// <returns>The value described in one line.</returns>
    public override string ToString() => $"{Key}={ValueText} ({Dataset})";
}

/// <summary>
/// One node of a telemetry filter tree: either a <see cref="ObservabilityTelemetryFilter"/> comparing a key
/// to a value, or a <see cref="ObservabilityTelemetryFilterGroup"/> combining other nodes.
/// </summary>
/// <remarks>
/// <para>
/// The wire is a union and this is its two arms. Which one a node is arrives as <c>kind</c>, so the
/// discriminator is written by the serializer rather than set by a caller: a filter carries
/// <c>"kind":"filter"</c> and a group <c>"kind":"group"</c>.
/// </para>
/// <para>
/// Groups nest, and the published schema caps that at <see cref="MaximumNestingDepth"/> levels.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ObservabilityTelemetryFilter), "filter")]
[JsonDerivedType(typeof(ObservabilityTelemetryFilterGroup), "group")]
public abstract class ObservabilityTelemetryFilterNode
{
    /// <summary>How deeply the published schema lets filter groups nest.</summary>
    public const int MaximumNestingDepth = 4;

    /// <summary>
    /// Checks a filter tree: that every leaf names a key, an operation and a value type, that every group
    /// says how it combines, and that nothing nests deeper than Cloudflare accepts.
    /// </summary>
    /// <param name="filters">The nodes to check, may be null.</param>
    /// <param name="problems">Collects one message per problem found.</param>
    /// <param name="depth">How deep these nodes sit, counting the outermost as 1.</param>
    internal static void Validate(
        IEnumerable<ObservabilityTelemetryFilterNode>? filters,
        List<string> problems,
        int depth = 1)
    {
        if (filters is null)
        {
            return;
        }

        foreach (var node in filters)
        {
            switch (node)
            {
                case ObservabilityTelemetryFilterGroup group:
                    // The cap is on how deeply groups nest, so a leaf sitting inside the fourth group is
                    // fine and a fifth group is not.
                    if (depth > MaximumNestingDepth)
                    {
                        problems.Add(
                            $"Filter groups nest at most {MaximumNestingDepth} deep, and this one is at " +
                            $"depth {depth}.");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(group.FilterCombination))
                    {
                        problems.Add(
                            "A filter group needs FilterCombination, to say whether its filters are combined " +
                            "with 'and' or 'or'.");
                    }

                    if (group.Filters is null)
                    {
                        problems.Add("A filter group needs Filters; a group of nothing filters nothing.");
                    }
                    else
                    {
                        Validate(group.Filters, problems, depth + 1);
                    }

                    break;

                case ObservabilityTelemetryFilter leaf:
                    leaf.Validate(problems);
                    break;
            }
        }
    }
}

/// <summary>
/// A filter leaf: one key, one operation, and the value to compare against.
/// </summary>
/// <remarks>
/// <para>
/// The operations are listed on <c>ObservabilityFilterOperations</c>. The published schema spells thirty of
/// them, including symbolic ones like <c>&gt;=</c> and uppercase ones the dashboard grew, and only the value
/// types are a closed set - so an operation is checked for being there and a type for being one of the three,
/// and an operation this library has not listed is passed through rather than refused.
/// </para>
/// <para>
/// <see cref="Value"/> is left out for the operations that ask whether a key exists at all.
/// </para>
/// </remarks>
public sealed class ObservabilityTelemetryFilter : ObservabilityTelemetryFilterNode
{
    /// <summary>The key to filter on.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>How to compare, one of <c>ObservabilityFilterOperations</c>.</summary>
    [JsonPropertyName("operation")]
    public string? Operation { get; set; }

    /// <summary>What the key's values are: one of <c>ObservabilityTelemetryValueTypes</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// The value to compare against: a string, a number or a boolean. Written as whichever it is, so a number
    /// reaches Cloudflare as a number rather than as a quoted one.
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    /// <summary>Builds a leaf filter.</summary>
    /// <param name="key">The key to filter on.</param>
    /// <param name="operation">One of <c>ObservabilityFilterOperations</c>.</param>
    /// <param name="type">One of <c>ObservabilityTelemetryValueTypes</c>.</param>
    /// <param name="value">The value to compare against, or null for an existence operation.</param>
    /// <returns>A leaf filter.</returns>
    public static ObservabilityTelemetryFilter Of(
        string key,
        string operation,
        string type,
        object? value = null) =>
        new()
        {
            Key = key,
            Operation = operation,
            Type = type,
            Value = value,
        };

    /// <summary>
    /// Checks the leaf: a key, an operation and one of the three value types.
    /// </summary>
    /// <param name="problems">Collects one message per problem found.</param>
    internal void Validate(List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            problems.Add("A filter needs a Key.");
        }

        if (string.IsNullOrWhiteSpace(Operation))
        {
            problems.Add(
                "A filter needs an Operation, one of ObservabilityFilterOperations - 'eq', 'includes', " +
                "'exists' and the rest.");
        }

        if (Type is not null &&
            Type != ObservabilityTelemetryValueTypes.String &&
            Type != ObservabilityTelemetryValueTypes.Number &&
            Type != ObservabilityTelemetryValueTypes.Boolean)
        {
            problems.Add(
                $"A filter's Type must be '{ObservabilityTelemetryValueTypes.String}', " +
                $"'{ObservabilityTelemetryValueTypes.Number}' or " +
                $"'{ObservabilityTelemetryValueTypes.Boolean}', but was '{Type}'.");
        }
        else if (Type is null)
        {
            problems.Add("A filter needs a Type, because the same key can hold different kinds of value.");
        }
    }

    /// <summary>Key, operation and value, on one line.</summary>
    /// <returns>The filter described in one line.</returns>
    public override string ToString() => $"{Key} {Operation} {Value}";
}

/// <summary>
/// A filter group: other nodes combined with <c>and</c> or <c>or</c>.
/// </summary>
public sealed class ObservabilityTelemetryFilterGroup : ObservabilityTelemetryFilterNode
{
    /// <summary>How the filters in this group combine: one of <c>ObservabilityFilterCombinations</c>.</summary>
    [JsonPropertyName("filterCombination")]
    public string? FilterCombination { get; set; }

    /// <summary>The nodes this group combines, which may themselves be groups.</summary>
    [JsonPropertyName("filters")]
    public List<ObservabilityTelemetryFilterNode>? Filters { get; set; }

    /// <summary>Builds a group whose filters all have to hold.</summary>
    /// <param name="filters">The nodes to combine.</param>
    /// <returns>An <c>and</c> group.</returns>
    public static ObservabilityTelemetryFilterGroup And(
        params ObservabilityTelemetryFilterNode[] filters) =>
        new()
        {
            FilterCombination = ObservabilityFilterCombinations.And,
            Filters = filters.ToList(),
        };

    /// <summary>Builds a group where one of the filters holding is enough.</summary>
    /// <param name="filters">The nodes to combine.</param>
    /// <returns>An <c>or</c> group.</returns>
    public static ObservabilityTelemetryFilterGroup Or(
        params ObservabilityTelemetryFilterNode[] filters) =>
        new()
        {
            FilterCombination = ObservabilityFilterCombinations.Or,
            Filters = filters.ToList(),
        };

    /// <summary>Combination and how many nodes it combines, on one line.</summary>
    /// <returns>The group described in one line.</returns>
    public override string ToString() => $"{FilterCombination}({Filters?.Count ?? 0})";
}

/// <summary>
/// A search term rather than a filter: match keys or values against it instead of comparing them exactly.
/// </summary>
/// <remarks>
/// The schema defines this shape twice, once for searching values (<c>needle</c>) and once for searching key
/// names (<c>keyNeedle</c>), and both are the same three fields.
/// </remarks>
public sealed class ObservabilityTelemetryNeedle
{
    /// <summary>The term to look for: a string, a number or a boolean.</summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    /// <summary>Whether <see cref="Value"/> is a regular expression rather than a literal.</summary>
    [JsonPropertyName("isRegex")]
    public bool? IsRegex { get; set; }

    /// <summary>Whether the search is case sensitive.</summary>
    [JsonPropertyName("matchCase")]
    public bool? MatchCase { get; set; }

    /// <summary>Builds a literal, case-insensitive search term.</summary>
    /// <param name="value">The term to look for.</param>
    /// <returns>A needle.</returns>
    public static ObservabilityTelemetryNeedle Literal(string value) => new() { Value = value };

    /// <summary>Builds a regular-expression search term.</summary>
    /// <param name="pattern">The expression to match.</param>
    /// <param name="matchCase">Whether matching is case sensitive.</param>
    /// <returns>A needle.</returns>
    public static ObservabilityTelemetryNeedle Regex(string pattern, bool matchCase = false) =>
        new()
        {
            Value = pattern,
            IsRegex = true,
            MatchCase = matchCase,
        };

    /// <summary>
    /// Checks that there is something to search for.
    /// </summary>
    /// <param name="where">What to call the needle in a problem message.</param>
    /// <param name="problems">Collects one message per problem found.</param>
    internal void Validate(string where, List<string> problems)
    {
        if (Value is null)
        {
            problems.Add($"{where} needs a Value: a search term with nothing in it matches nothing.");
        }
    }

    /// <summary>The term and how it is matched, on one line.</summary>
    /// <returns>The needle described in one line.</returns>
    public override string ToString() =>
        IsRegex == true ? $"/{Value}/{(MatchCase == true ? "case-sensitive" : "case-insensitive")}" : $"{Value}";
}

/// <summary>A span of time, in the milliseconds since the epoch that the telemetry endpoints count in.</summary>
/// <remarks>
/// Both ends are numbers on the wire and the published schema does not say which unit they count in; a live
/// <c>lastSeenAt</c> of <c>1793943698985</c> for a key seen that day settled it as milliseconds, and
/// <see cref="Between(DateTimeOffset, DateTimeOffset)"/> converts so a caller does not have to.
/// </remarks>
public sealed class ObservabilityTelemetryTimeframe
{
    /// <summary>Start of the span.</summary>
    [JsonPropertyName("from")]
    public long? From { get; set; }

    /// <summary>End of the span.</summary>
    [JsonPropertyName("to")]
    public long? To { get; set; }

    /// <summary>Builds a span from two epoch timestamps in milliseconds.</summary>
    /// <param name="from">Start of the span.</param>
    /// <param name="to">End of the span.</param>
    /// <returns>A timeframe.</returns>
    public static ObservabilityTelemetryTimeframe Between(long from, long to) => new() { From = from, To = to };

    /// <summary>Builds a span from two dates, converted to the milliseconds the endpoint counts in.</summary>
    /// <param name="from">Start of the span.</param>
    /// <param name="to">End of the span.</param>
    /// <returns>A timeframe.</returns>
    public static ObservabilityTelemetryTimeframe Between(DateTimeOffset from, DateTimeOffset to) =>
        new()
        {
            From = ToMilliseconds(from),
            To = ToMilliseconds(to),
        };

    /// <summary>Converts a date to the epoch milliseconds these endpoints count in.</summary>
    /// <param name="timestamp">The date to convert.</param>
    /// <returns>Milliseconds since the epoch.</returns>
    internal static long ToMilliseconds(DateTimeOffset timestamp) =>
        (long)(timestamp.ToUniversalTime() - DateTimeOffset.UnixEpoch).TotalMilliseconds;

    /// <summary>
    /// Checks that both ends are there and in the right order.
    /// </summary>
    /// <param name="problems">Collects one message per problem found.</param>
    internal void Validate(List<string> problems)
    {
        if (From is null || To is null)
        {
            problems.Add("A Timeframe needs both From and To.");
        }
        else if (To < From)
        {
            problems.Add($"Timeframe.To ({To}) is before Timeframe.From ({From}), which is an empty span.");
        }
    }

    /// <summary>The two ends of the span, on one line.</summary>
    /// <returns>The timeframe described in one line.</returns>
    public override string ToString() => $"{From}..{To}";
}

/// <summary>The value types a telemetry key can carry.</summary>
public static class ObservabilityTelemetryValueTypes
{
    /// <summary>A string value.</summary>
    public const string String = "string";

    /// <summary>A number value.</summary>
    public const string Number = "number";

    /// <summary>A boolean value.</summary>
    public const string Boolean = "boolean";
}

/// <summary>How a filter group combines its filters.</summary>
/// <remarks>
/// Cloudflare accepts these in either case, and so does the schema; the lowercase spellings are the ones used
/// here.
/// </remarks>
public static class ObservabilityFilterCombinations
{
    /// <summary>Every filter in the group has to hold.</summary>
    public const string And = "and";

    /// <summary>One filter in the group holding is enough.</summary>
    public const string Or = "or";
}

/// <summary>The operations a filter leaf accepts.</summary>
/// <remarks>
/// The published schema lists all of these, and they are not one family: the word ones, the symbolic ones the
/// same comparisons spelled as operators, and a set of uppercase ones the dashboard grew. A filter's
/// operation is not validated against this list, because refusing a spelling Cloudflare accepts would be
/// worse than passing it through.
/// </remarks>
public static class ObservabilityFilterOperations
{
    /// <summary>The value contains the given text.</summary>
    public const string Includes = "includes";

    /// <summary>The value does not contain the given text.</summary>
    public const string NotIncludes = "not_includes";

    /// <summary>The value starts with the given text.</summary>
    public const string StartsWith = "starts_with";

    /// <summary>The value ends with the given text.</summary>
    public const string EndsWith = "ends_with";

    /// <summary>The value matches the given expression.</summary>
    public const string Regex = "regex";

    /// <summary>The key is present.</summary>
    public const string Exists = "exists";

    /// <summary>The key is present and null.</summary>
    public const string IsNull = "is_null";

    /// <summary>The value is one of a set.</summary>
    public const string In = "in";

    /// <summary>The value is not one of a set.</summary>
    public const string NotIn = "not_in";

    /// <summary>Equal.</summary>
    public const string Equal = "eq";

    /// <summary>Not equal.</summary>
    public const string NotEqual = "neq";

    /// <summary>Greater than.</summary>
    public const string GreaterThan = "gt";

    /// <summary>Greater than or equal.</summary>
    public const string GreaterThanOrEqual = "gte";

    /// <summary>Less than.</summary>
    public const string LessThan = "lt";

    /// <summary>Less than or equal.</summary>
    public const string LessThanOrEqual = "lte";

    /// <summary>Equal, spelled as an operator.</summary>
    public const string EqualsOperator = "=";

    /// <summary>Not equal, spelled as an operator.</summary>
    public const string NotEqualsOperator = "!=";

    /// <summary>Greater than, spelled as an operator.</summary>
    public const string GreaterThanOperator = ">";

    /// <summary>Greater than or equal, spelled as an operator.</summary>
    public const string GreaterThanOrEqualOperator = ">=";

    /// <summary>Less than, spelled as an operator.</summary>
    public const string LessThanOperator = "<";

    /// <summary>Less than or equal, spelled as an operator.</summary>
    public const string LessThanOrEqualOperator = "<=";

    /// <summary>The value contains the given text, in the dashboard's spelling.</summary>
    public const string IncludesUpper = "INCLUDES";

    /// <summary>The value does not contain the given text, in the dashboard's spelling.</summary>
    public const string DoesNotInclude = "DOES_NOT_INCLUDE";

    /// <summary>The value matches the given expression, in the dashboard's spelling.</summary>
    public const string MatchRegex = "MATCH_REGEX";

    /// <summary>The key is present, in the dashboard's spelling.</summary>
    public const string ExistsUpper = "EXISTS";

    /// <summary>The key is absent, which the word family has no spelling for.</summary>
    public const string DoesNotExist = "DOES_NOT_EXIST";

    /// <summary>The value is one of a set, in the dashboard's spelling.</summary>
    public const string InUpper = "IN";

    /// <summary>The value is not one of a set, in the dashboard's spelling.</summary>
    public const string NotInUpper = "NOT_IN";

    /// <summary>The value starts with the given text, in the dashboard's spelling.</summary>
    public const string StartsWithUpper = "STARTS_WITH";

    /// <summary>The value ends with the given text, in the dashboard's spelling.</summary>
    public const string EndsWithUpper = "ENDS_WITH";
}

/// <summary>
/// Asks which attribute keys have appeared in an account's telemetry.
/// </summary>
/// <remarks>
/// The body of <c>POST /accounts/{account_id}/workers/observability/telemetry/keys</c>. Every field is
/// optional: an empty body asks for the keys in the default datasets over the default span.
/// </remarks>
public sealed class ObservabilityTelemetryKeysRequest
{
    /// <summary>Which datasets to look in. Empty means the defaults.</summary>
    [JsonPropertyName("datasets")]
    public List<string>? Datasets { get; set; }

    /// <summary>Filters narrowing which keys are wanted.</summary>
    [JsonPropertyName("filters")]
    public List<ObservabilityTelemetryFilterNode>? Filters { get; set; }

    /// <summary>Start of the span to look over, as an epoch timestamp.</summary>
    [JsonPropertyName("from")]
    public long? From { get; set; }

    /// <summary>End of the span to look over, as an epoch timestamp.</summary>
    [JsonPropertyName("to")]
    public long? To { get; set; }

    /// <summary>How many keys to answer with.</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    /// <summary>A search term matched against the keys' values.</summary>
    [JsonPropertyName("needle")]
    public ObservabilityTelemetryNeedle? Needle { get; set; }

    /// <summary>A search term matched against the keys' names.</summary>
    [JsonPropertyName("keyNeedle")]
    public ObservabilityTelemetryNeedle? KeyNeedle { get; set; }

    /// <summary>Builds a request that looks over a span of time.</summary>
    /// <param name="from">Start of the span.</param>
    /// <param name="to">End of the span.</param>
    /// <returns>A request with <see cref="From"/> and <see cref="To"/> set in milliseconds.</returns>
    public static ObservabilityTelemetryKeysRequest OverSpan(DateTimeOffset from, DateTimeOffset to) =>
        new()
        {
            From = ObservabilityTelemetryTimeframe.ToMilliseconds(from),
            To = ObservabilityTelemetryTimeframe.ToMilliseconds(to),
        };

    /// <summary>
    /// Checks the request: the filters against the shape Cloudflare accepts, the span's order, and the limit.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">Something would be refused.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        ObservabilityTelemetryFilterNode.Validate(Filters, problems);

        if (From is { } from && To is { } to && to < from)
        {
            problems.Add($"To ({to}) is before From ({from}), which is an empty span.");
        }

        if (Limit is < 1)
        {
            problems.Add($"Limit must be at least 1, but was {Limit}.");
        }

        Needle?.Validate("Needle", problems);
        KeyNeedle?.Validate("KeyNeedle", problems);

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }
}

/// <summary>
/// Asks which values one attribute key has been seen with.
/// </summary>
/// <remarks>
/// The body of <c>POST /accounts/{account_id}/workers/observability/telemetry/values</c>. The key, its type
/// and a timeframe are required; the datasets are required to be present but may be empty, which the schema
/// documents as meaning the defaults.
/// </remarks>
public sealed class ObservabilityTelemetryValuesRequest
{
    /// <summary>Which datasets to look in. Required, and empty means the defaults.</summary>
    [JsonPropertyName("datasets")]
    public List<string>? Datasets { get; set; }

    /// <summary>The key to list the values of.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>The span to look over.</summary>
    [JsonPropertyName("timeframe")]
    public ObservabilityTelemetryTimeframe? Timeframe { get; set; }

    /// <summary>What the key's values are: one of <c>ObservabilityTelemetryValueTypes</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Filters narrowing which values are wanted.</summary>
    [JsonPropertyName("filters")]
    public List<ObservabilityTelemetryFilterNode>? Filters { get; set; }

    /// <summary>How many values to answer with.</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    /// <summary>A search term matched against the values.</summary>
    [JsonPropertyName("needle")]
    public ObservabilityTelemetryNeedle? Needle { get; set; }

    /// <summary>
    /// Checks the request against what the published schema requires.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">Something required is missing.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Key))
        {
            problems.Add("Key is required: this endpoint lists the values of one key.");
        }

        if (Datasets is null)
        {
            problems.Add(
                "Datasets is required, though it may be empty - an empty list means the default datasets, " +
                "and leaving it out is not the same request.");
        }

        if (Timeframe is null)
        {
            problems.Add("Timeframe is required: the values of a key are the ones seen in some span.");
        }
        else
        {
            Timeframe.Validate(problems);
        }

        if (Type is null)
        {
            problems.Add("Type is required: one of ObservabilityTelemetryValueTypes.");
        }
        else if (Type != ObservabilityTelemetryValueTypes.String &&
                 Type != ObservabilityTelemetryValueTypes.Number &&
                 Type != ObservabilityTelemetryValueTypes.Boolean)
        {
            problems.Add(
                $"Type must be '{ObservabilityTelemetryValueTypes.String}', " +
                $"'{ObservabilityTelemetryValueTypes.Number}' or " +
                $"'{ObservabilityTelemetryValueTypes.Boolean}', but was '{Type}'.");
        }

        ObservabilityTelemetryFilterNode.Validate(Filters, problems);

        if (Limit is < 1)
        {
            problems.Add($"Limit must be at least 1, but was {Limit}.");
        }

        Needle?.Validate("Needle", problems);

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }
}

/// <summary>
/// Asks Cloudflare to prepare a WebSocket server for a live tail of telemetry.
/// </summary>
/// <remarks>
/// The body of <c>POST /accounts/{account_id}/workers/observability/telemetry/live-tail</c>. Every field is
/// optional, so an empty body asks for a tail of everything the account can see.
/// </remarks>
public sealed class ObservabilityLiveTailRequest
{
    /// <summary>Restricts the tail to one Worker, by its script id.</summary>
    [JsonPropertyName("scriptId")]
    public string? ScriptId { get; set; }

    /// <summary>How <see cref="Filters"/> combine: one of <c>ObservabilityFilterCombinations</c>.</summary>
    [JsonPropertyName("filterCombination")]
    public string? FilterCombination { get; set; }

    /// <summary>Filters narrowing what the tail carries.</summary>
    [JsonPropertyName("filters")]
    public List<ObservabilityTelemetryFilterNode>? Filters { get; set; }

    /// <summary>
    /// Checks the filters against the shape Cloudflare accepts.
    /// </summary>
    /// <exception cref="Exceptions.CloudflareConfigurationException">A filter would be refused.</exception>
    public void EnsureValid()
    {
        var problems = new List<string>();

        ObservabilityTelemetryFilterNode.Validate(Filters, problems);

        if (problems.Count > 0)
        {
            throw new Exceptions.CloudflareConfigurationException(problems);
        }
    }
}

/// <summary>
/// A prepared live tail: the WebSocket address its events are streamed from.
/// </summary>
/// <remarks>
/// Preparing a tail is a REST call; the events themselves arrive over the WebSocket at <see cref="WsUrl"/>,
/// which is not something this library opens. The session has to be kept alive - see
/// <c>IWorkersObservabilityService.SendLiveTailHeartbeatAsync</c> - and a script's own
/// <c>WorkerTail</c> is the other, log-shaped way to watch a Worker.
/// </remarks>
public sealed class ObservabilityLiveTail
{
    /// <summary>The WebSocket address clients connect to for this tail's events.</summary>
    [JsonPropertyName("wsUrl")]
    public string? WsUrl { get; set; }

    /// <summary>Captures any field Cloudflare adds in the future.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>The WebSocket address, on one line.</summary>
    /// <returns>The live tail described in one line.</returns>
    public override string ToString() => $"{WsUrl}";
}

/// <summary>Keeps a prepared live tail session alive.</summary>
/// <remarks>
/// The body of
/// <c>POST /accounts/{account_id}/workers/observability/telemetry/live-tail/heartbeat</c>, which answers with
/// no result of its own: a heartbeat is either accepted or it is not, and a session that stops getting them
/// is dropped.
/// </remarks>
public sealed class ObservabilityLiveTailHeartbeatRequest
{
    /// <summary>The Worker the session is tailing, when it was prepared for one.</summary>
    [JsonPropertyName("scriptId")]
    public string? ScriptId { get; set; }
}
