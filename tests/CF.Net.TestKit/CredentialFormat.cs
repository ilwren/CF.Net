using System.Text.RegularExpressions;

namespace CFNet.TestKit;

/// <summary>
/// Tells an obviously malformed Cloudflare credential from a plausible one, so a run can say "that is
/// not an account id" instead of failing every test with an authentication error.
/// </summary>
/// <remarks>
/// <para>
/// These are shape checks, not validation. Cloudflare is the only authority on whether a credential
/// works, which is what <c>GET /user/tokens/verify</c> is for. The point here is to catch the mistakes
/// a shape can catch: a pasted dashboard URL, a value copied with its quotes still attached, an
/// account name where an id belongs, or an API token pasted into the global-key variable.
/// </para>
/// <para>
/// Shapes as of 2026. Account and zone ids are 32 hexadecimal characters. New credentials are
/// prefixed so that secret scanners recognise them — <c>cfut_</c> for a user API token, <c>cfat_</c>
/// for an account-owned API token and <c>cfk_</c> for the Global API Key, each followed by 40
/// characters and a checksum. Credentials issued before that are unprefixed: a 40-character
/// alphanumeric token, or a 37-45 character lowercase hexadecimal global key. Both generations still
/// work, so both are accepted here.
/// </para>
/// </remarks>
public static class CredentialFormat
{
    private static readonly Regex IdPattern = new("^[0-9a-fA-F]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PrefixedPattern = new("^cf(ut|at|k)_[A-Za-z0-9_-]{20,120}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LegacyTokenPattern = new("^[A-Za-z0-9_-]{40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LegacyGlobalKeyPattern = new("^[0-9a-f]{37,45}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmailPattern = new("^[^@]+@[^@.]+[.][^@.]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Length beyond which a value is described rather than quoted.</summary>
    private const int QuotedLength = 60;

    /// <summary>
    /// Normalises a pasted value: trims it, and removes one layer of surrounding quotes.
    /// </summary>
    /// <remarks>
    /// Copying a credential out of a JSON file or a shell export usually brings the quotes with it,
    /// and a quoted token fails authentication with an error that gives no hint why.
    /// </remarks>
    /// <param name="value">The raw value; may be <see langword="null"/>.</param>
    /// <returns>The value to judge, or <see langword="null"/> when there was nothing there.</returns>
    public static string? Normalise(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
             (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>Whether the value has the shape of a Cloudflare account or zone id.</summary>
    /// <param name="value">The configured value.</param>
    public static bool IsWellFormedId(string? value) =>
        Normalise(value) is string id && IdPattern.IsMatch(id);

    /// <summary>Whether the value has the shape of an API token, user owned or account owned.</summary>
    /// <param name="value">The configured value.</param>
    public static bool IsWellFormedApiToken(string? value)
    {
        var token = Normalise(value);
        if (token is null)
        {
            return false;
        }

        if (token.StartsWith("cfk_", StringComparison.Ordinal))
        {
            // That is a Global API Key, which belongs in the other variable.
            return false;
        }

        if (token.StartsWith("cfut_", StringComparison.Ordinal) || token.StartsWith("cfat_", StringComparison.Ordinal))
        {
            // Recognised prefix, so judge it as a new-format credential: a truncated paste should not
            // slip through just because it starts the right way.
            return PrefixedPattern.IsMatch(token);
        }

        return LegacyTokenPattern.IsMatch(token);
    }

    /// <summary>Whether the value has the shape of a Global API Key.</summary>
    /// <param name="value">The configured value.</param>
    public static bool IsWellFormedGlobalApiKey(string? value)
    {
        var key = Normalise(value);
        if (key is null)
        {
            return false;
        }

        if (key.StartsWith("cfut_", StringComparison.Ordinal) || key.StartsWith("cfat_", StringComparison.Ordinal))
        {
            // That is an API token, which belongs in the other variable.
            return false;
        }

        if (key.StartsWith("cfk_", StringComparison.Ordinal))
        {
            return PrefixedPattern.IsMatch(key);
        }

        return LegacyGlobalKeyPattern.IsMatch(key);
    }

    /// <summary>Whether the value has the shape of an e-mail address.</summary>
    /// <param name="value">The configured value.</param>
    public static bool IsWellFormedEmail(string? value) =>
        Normalise(value) is string email && EmailPattern.IsMatch(email);

    /// <summary>
    /// Reports a value that <see cref="Normalise"/> had to clean up. The fixture configures the client
    /// with what was actually set, not with the cleaned copy, so a quoted paste has to be fixed by the
    /// human rather than quietly repaired here.
    /// </summary>
    /// <param name="value">The raw configured value.</param>
    /// <param name="normalised">What it looks like once cleaned up.</param>
    /// <param name="name">What to call it in the message, usually the variable name.</param>
    /// <returns>The warning, or <see langword="null"/> when the value needed no cleaning.</returns>
    private static string? ExplainQuoting(string value, string normalised, string name) =>
        string.Equals(value, normalised, StringComparison.Ordinal)
            ? null
            : $"{name} has quotes or extra whitespace around it. Paste the bare value: Cloudflare compares it exactly, and nothing strips the quotes for you.";

    /// <summary>
    /// Describes what is wrong with an account or zone id, or returns <see langword="null"/> when it
    /// looks right. An absent value is not malformed — "it is required" is a different message, and
    /// the caller already has one.
    /// </summary>
    /// <param name="value">The configured value.</param>
    /// <param name="name">What to call it in the message, usually the variable name.</param>
    /// <param name="hint">Where the real value is found, appended to the message.</param>
    public static string? ExplainId(string? value, string name, string hint)
    {
        var id = Normalise(value);
        if (id is null)
        {
            return null;
        }

        if (ExplainQuoting(value!, id, name) is string quoting)
        {
            // An id is not a secret, so showing the cleaned value is the fastest way to confirm it.
            return $"{quoting} Without them it reads {Quote(id)}.";
        }

        if (IdPattern.IsMatch(id))
        {
            return null;
        }

        return $"{name} does not look like a Cloudflare id: expected 32 hexadecimal characters, got {Quote(id)}. {hint}";
    }

    /// <summary>
    /// Describes what is wrong with an API token, or returns <see langword="null"/> when it looks
    /// right. The value itself is never echoed.
    /// </summary>
    /// <param name="value">The configured value.</param>
    /// <param name="name">What to call it in the message, usually the variable name.</param>
    public static string? ExplainApiToken(string? value, string name = "CLOUDFLARE_API_TOKEN")
    {
        var token = Normalise(value);
        if (token is null)
        {
            return null;
        }

        if (ExplainQuoting(value!, token, name) is string quoting)
        {
            return quoting;
        }

        if (IsWellFormedApiToken(token))
        {
            return null;
        }

        if (token.StartsWith("cfk_", StringComparison.Ordinal) || LegacyGlobalKeyPattern.IsMatch(token))
        {
            return $"{name} looks like a Global API Key, not a scoped token. Either put it in CLOUDFLARE_GLOBAL_API_KEY together with CLOUDFLARE_API_EMAIL, or create an API token instead - a token can be scoped, a global key cannot.";
        }

        if (IdPattern.IsMatch(token))
        {
            return $"{name} looks like an account or zone id ({Quote(token)}), not a credential.";
        }

        if (token.Contains("://") || token.Contains('/'))
        {
            return $"{name} looks like a URL. Copy only the secret shown once, on the page after 'Create Token'.";
        }

        if (token.Contains(' '))
        {
            return $"{name} contains a space: copy the token alone, without the label or the surrounding text.";
        }

        return $"{name} is not a shape Cloudflare issues ({DescribeSecret(token)}). Expected 'cfut_' or 'cfat_' followed by 40 characters and a checksum, or a pre-2026 40-character alphanumeric token.";
    }

    /// <summary>
    /// Describes what is wrong with a Global API Key, or returns <see langword="null"/> when it looks
    /// right. The value itself is never echoed.
    /// </summary>
    /// <param name="value">The configured value.</param>
    /// <param name="name">What to call it in the message, usually the variable name.</param>
    public static string? ExplainGlobalApiKey(string? value, string name = "CLOUDFLARE_GLOBAL_API_KEY")
    {
        var key = Normalise(value);
        if (key is null)
        {
            return null;
        }

        if (ExplainQuoting(value!, key, name) is string quoting)
        {
            return quoting;
        }

        if (IsWellFormedGlobalApiKey(key))
        {
            return null;
        }

        if (key.StartsWith("cfut_", StringComparison.Ordinal) || key.StartsWith("cfat_", StringComparison.Ordinal))
        {
            return $"{name} looks like an API token. Put it in CLOUDFLARE_API_TOKEN instead, and leave the global key unset.";
        }

        if (LegacyTokenPattern.IsMatch(key))
        {
            return $"{name} looks like a pre-2026 API token. Put it in CLOUDFLARE_API_TOKEN instead.";
        }

        return $"{name} is not a shape Cloudflare issues ({DescribeSecret(key)}). Expected 'cfk_' followed by 40 characters and a checksum, or a pre-2026 37-45 character lowercase hexadecimal key.";
    }

    /// <summary>
    /// Describes what is wrong with the e-mail address that accompanies a Global API Key, or returns
    /// <see langword="null"/> when it looks right.
    /// </summary>
    /// <param name="value">The configured value.</param>
    /// <param name="name">What to call it in the message, usually the variable name.</param>
    public static string? ExplainEmail(string? value, string name = "CLOUDFLARE_API_EMAIL")
    {
        var email = Normalise(value);
        if (email is null)
        {
            return null;
        }

        if (ExplainQuoting(value!, email, name) is string quoting)
        {
            return quoting;
        }

        if (EmailPattern.IsMatch(email))
        {
            return null;
        }

        // Described rather than quoted: an address is not a credential, but it is personal, and this
        // message can end up in a CI summary on a public repository.
        var shape = email.Contains('@')
            ? $"a {email.Length}-character address with no domain part"
            : $"a {email.Length}-character value with no '@' in it";

        return $"{name} does not look like an e-mail address ({shape}). It has to be the address the Cloudflare account logs in with, because the legacy X-Auth-Email header is matched against it.";
    }

    /// <summary>
    /// Quotes a non-secret value so a message can show it, shortening anything long.
    /// </summary>
    /// <param name="value">An id, an address - never a credential.</param>
    private static string Quote(string value) =>
        value.Length <= QuotedLength
            ? $"'{value}'"
            : $"{value.Length} characters starting '{value[..12]}...'";

    /// <summary>
    /// Describes a credential by its length and prefix only. Printing a secret into a log, a test
    /// report or a CI summary is exactly what this class exists to avoid.
    /// </summary>
    /// <param name="secret">The credential to describe.</param>
    private static string DescribeSecret(string secret)
    {
        var knownPrefixes = new[] { "cfut_", "cfat_", "cfk_" };
        foreach (var prefix in knownPrefixes)
        {
            if (secret.StartsWith(prefix, StringComparison.Ordinal))
            {
                return $"a {secret.Length}-character value starting '{prefix}'";
            }
        }

        return $"an unprefixed {secret.Length}-character value";
    }
}
