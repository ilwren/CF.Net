# Cassettes

A cassette is a JSON recording of real Cloudflare API traffic: one file per run, one entry per
request/response pair. Its job is to close the gap between the two test suites.

| Suite | Talks to Cloudflare? | Needs credentials? | Payloads |
| --- | --- | --- | --- |
| `tests/CF.Net.UnitTests` | No | No | Hand-written, plus **replayed cassettes** |
| `tests/CF.Net.IntegrationTests` | Yes | Yes | Live |

The integration suite proves the client works against the real API today. A cassette freezes that
proof so the unit suite can keep asserting against genuine payloads - offline, in CI, on every pull
request, for free.

## Recording

Set a cassette name and turn recording on; credentials are still required, because this is a real run.

```bash
export CLOUDFLARE_API_TOKEN='...'
export CLOUDFLARE_ACCOUNT_ID='...'
export CLOUDFLARE_ZONE_ID='...'
export CLOUDFLARE_IT_CASSETTE=readonly-smoke
export CLOUDFLARE_IT_RECORD=true          # implied whenever CLOUDFLARE_IT_CASSETTE is set
export CLOUDFLARE_IT_ENABLE_WRITES=true   # only if you want writes in the recording

dotnet test tests/CF.Net.IntegrationTests --filter "Category=Integration"
```

Or from GitHub Actions: run the **Integration** workflow manually and fill in the `cassette` input.
The recorded file is attached to the run as the `cassette-<name>` artifact, and with
`commit_cassette` enabled the workflow commits it straight back to the branch.

Recording merges into an existing file rather than truncating it, so a filtered run adds to a
cassette instead of replacing it.

## What gets stored

```json
{
  "name": "readonly-smoke",
  "baseUrl": "https://api.cloudflare.com/client/v4/",
  "recordedAt": "2026-10-11T00:00:00Z",
  "exchanges": [
    {
      "name": "VerifyToken_returns_the_status",
      "method": "GET",
      "pathAndQuery": "/client/v4/user/tokens/verify",
      "requestHeaders": { "Authorization": "<redacted>", "User-Agent": "CF.Net/0.1.0 (...)" },
      "requestBody": null,
      "statusCode": 200,
      "responseHeaders": { "CF-RAY": "..." },
      "responseContentType": "application/json; charset=utf-8",
      "responseBody": "{\"success\":true,\"errors\":[],\"messages\":[],\"result\":{...}}",
      "recordedAt": "2026-10-11T09:14:02.117Z"
    }
  ]
}
```

Credentials never survive: `Authorization`, `X-Auth-Key`, `X-Auth-Email`, `X-Api-Key`, `Cookie` and
`Set-Cookie` are replaced with `<redacted>` on the way in, and `CassetteTests` fails the build if a
committed cassette carries a real value for any of them. Bodies that are not valid UTF-8 are stored
as base64 so binary payloads (Worker content, KV values) round-trip exactly.

**Everything else is a real API payload.** Account ids, zone ids, Worker names, KV values and DNS
records all land in the file. Read the diff before you commit it, and do not record from an account
whose contents you would not publish.

## Replaying

`CassetteHandler.Replaying(cassette)` answers from the file and refuses to open a socket: its inner
handler throws, so a missing entry is a `CassetteMissException`, never a silent network call.

```csharp
var cassette = Cassette.Load(RepoPaths.CassettePath("readonly-smoke"));
using var http = new HttpClient(CassetteHandler.Replaying(cassette));
var options = new CloudflareClientOptions { ApiToken = "replay-does-not-need-a-real-token" };
using var transport = new CloudflareTransport(http, options);
var client = new CloudflareClient(transport);

var zones = await client.Zones.ListAsync();   // served entirely from the cassette
```

`CassetteTests.Committed_cassettes_bind_to_the_typed_models` does this for every cassette in this
directory: it parses each recorded envelope and binds `result` to the strongly typed model
(`Zone`, `DnsRecord`, `WorkerScript`, `PagesProject`, `D1Database`, `KvNamespace`,
`R2BucketListResult`, ...). That is the drift detector - if Cloudflare renames a field in a way that
breaks a model, a committed cassette turns it into an offline CI failure instead of a production
surprise. It is a no-op while this directory holds no cassettes.
