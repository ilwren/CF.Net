using System.Text.RegularExpressions;

namespace CFNet.TestKit;

/// <summary>
/// Recovers the non-secret identifiers a cassette was recorded with, so a replay can rebuild the same
/// request URLs without anyone having to configure them all over again.
/// </summary>
/// <remarks>
/// <para>
/// A cassette answers a call by matching <see cref="RecordedExchange.MatchKey"/>, which is the method
/// plus the path and query. Those paths contain the account and zone ids, so a replay only matches
/// when the client is configured with the same ones. Neither id is a secret - both sit in the
/// dashboard URL - but asking a developer, or a CI job, to export them again just to watch a
/// recording is needless friction when the recording already carries the answer.
/// </para>
/// <para>
/// Credentials are never recovered. They are redacted on the way in, and a replay authenticates
/// against nothing at all: <see cref="CassetteHandler"/> in replay mode sits on a handler that throws
/// rather than opening a socket.
/// </para>
/// </remarks>
public static class CassetteContext
{
    /// <summary>Cloudflare ids are 32 hexadecimal characters.</summary>
    private const string IdPattern = "[0-9a-fA-F]{32}";

    /// <summary>
    /// The first account id appearing in the recorded paths, or <see langword="null"/> when the
    /// cassette contains no account-scoped call.
    /// </summary>
    /// <param name="cassette">The recording to look through; may be <see langword="null"/>.</param>
    public static string? DeriveAccountId(Cassette? cassette) => First(cassette, "accounts");

    /// <summary>
    /// The first zone id appearing in the recorded paths, or <see langword="null"/> when the cassette
    /// contains no zone-scoped call.
    /// </summary>
    /// <param name="cassette">The recording to look through; may be <see langword="null"/>.</param>
    public static string? DeriveZoneId(Cassette? cassette) => First(cassette, "zones");

    /// <summary>Both identifiers at once, which is how a run usually wants to log them.</summary>
    /// <param name="cassette">The recording to look through; may be <see langword="null"/>.</param>
    public static (string? AccountId, string? ZoneId) Derive(Cassette? cassette) =>
        (DeriveAccountId(cassette), DeriveZoneId(cassette));

    /// <summary>
    /// Finds the first <c>/{segment}/{id}</c> in the recorded paths.
    /// </summary>
    /// <remarks>
    /// Only a path segment counts: <c>/zones?per_page=20</c> is the list endpoint and carries no id,
    /// while <c>/zones/{zone}/dns_records/{record}</c> yields the zone, which is the id a replay has
    /// to reproduce. The trailing boundary keeps a longer token from being truncated into a match.
    /// </remarks>
    private static string? First(Cassette? cassette, string segment)
    {
        if (cassette is null)
        {
            return null;
        }

        var pattern = new Regex($"/{segment}/({IdPattern})(?:[/?#]|$)", RegexOptions.CultureInvariant);
        foreach (var exchange in cassette.Exchanges)
        {
            var match = pattern.Match(exchange.PathAndQuery ?? string.Empty);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }
}
