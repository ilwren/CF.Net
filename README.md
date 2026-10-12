# CF.Net

A strongly typed C# client for the [Cloudflare REST API v4](https://developers.cloudflare.com/api/) — `https://api.cloudflare.com/client/v4`.

Every endpoint is a method on a service, every request and response is a data model, and the URL, query string, headers and JSON body are assembled for you. Targets **.NET 8** and **.NET 10**, registers into `IServiceCollection`, ships with an offline test suite and an Avalonia gallery you can point at your own account.

```csharp
using CFNet;

var client = CloudflareClient.CreateFromEnvironment();   // CLOUDFLARE_API_TOKEN, CLOUDFLARE_ACCOUNT_ID, ...

await foreach (var zone in client.Zones.ListAllAsync())
{
    Console.WriteLine($"{zone.Name}  [{zone.Status}]  ns={string.Join(", ", zone.NameServers ?? [])}");
}

var record = await client.DnsRecords.CreateAddressRecordAsync("www.example.com", "192.0.2.1", proxied: true);
var answer = await client.D1.QueryAsync(databaseId, "SELECT id, label FROM items WHERE id > ?", [10]);
Console.WriteLine(answer.Rows<Item>()[0].Label);
```

---

## Contents

- [Install](#install)
- [Getting started](#getting-started)
- [Authentication](#authentication)
- [Dependency injection](#dependency-injection)
- [What is covered](#what-is-covered)
- [Design notes](#design-notes)
- [Errors, retries and pagination](#errors-retries-and-pagination)
- [Testing](#testing)
- [The Avalonia gallery](#the-avalonia-gallery)
- [Integration tests in GitHub Actions](#integration-tests-in-github-actions)
- [Repository layout](#repository-layout)

---

## Install

```bash
dotnet add package CF.Net
```

Or reference the project directly:

```xml
<ProjectReference Include="..\CF.Net\src\CF.Net\CF.Net.csproj" />
```

The library has exactly two package dependencies — `Microsoft.Extensions.Http` and `Microsoft.Extensions.Options.ConfigurationExtensions`, both on the 8.0.x line so they work on net8.0 and net10.0 alike. Everything else (`Options`, `Logging.Abstractions`, `DependencyInjection.Abstractions`, `Configuration.Binder`, …) flows in transitively, so there is no chance of pinning a sibling package below what it requires.

> **Namespace note.** The root namespace is `CFNet`, not `CF.Net`. A namespace whose segments match a type name (`CF.Net.Models`) makes the compiler resolve `CF` as a type in some positions. `AssemblyName` and `PackageId` remain `CF.Net`.

---

## Getting started

### Without dependency injection

```csharp
using CFNet;
using CFNet.Configuration;

using var client = CloudflareClient.Create(options =>
{
    options.ApiToken  = "v1.0-abc...";
    options.AccountId = "023e105f4ecef8ad9ca31a8372d0c353";
    options.ZoneId    = "372e67954025e0ba6aaa6d586b9e0b59";

    options.Timeout       = TimeSpan.FromSeconds(30);
    options.MaxRetries    = 3;              // 408, 429 and 5xx only
    options.ThrowOnApiError = true;         // set false to inspect responses instead
});

var zones = await client.Zones.ListAsync(new ZoneListParameters
{
    Status  = ZoneStatusValues.Active,
    PerPage = 50,
});

Console.WriteLine($"{zones.Count} of {zones.TotalCount} zones");
```

`CloudflareClient.CreateFromEnvironment()` does the same thing from environment variables, and `CloudflareClientFactory` is available when you need several clients over one set of defaults.

### The nine services

| Property | Interface | Covers |
| --- | --- | --- |
| `client.User` | `IUserService` | token verification, the authenticated user, connectivity check |
| `client.Accounts` | `IAccountsService` | account discovery |
| `client.Zones` | `IZonesService` | zones, activation checks, cache purge |
| `client.DnsRecords` | `IDnsRecordsService` | DNS records, including typed SRV/LOC/MX data |
| `client.Workers` | `IWorkersScriptsService` | scripts, module content, script settings, deployments, `workers.dev` subdomain |
| `client.Pages` | `IPagesProjectsService` | projects, deployments, custom domains |
| `client.D1` | `ID1DatabaseService` | databases, SQL queries, batches, backups |
| `client.Kv` | `IKvService` | namespaces, key listings, values, bulk writes |
| `client.R2` | `IR2Service` | buckets, including jurisdiction-restricted ones |

---

## Authentication

Both schemes Cloudflare supports are available, and the library sends **exactly one** of them per call.

| Scheme | Headers | Configure |
| --- | --- | --- |
| **API token** (recommended) | `Authorization: Bearer <token>` | `options.ApiToken` |
| **Global API key** (legacy) | `X-Auth-Key: <key>` + `X-Auth-Email: <email>` | `options.GlobalApiKey` + `options.ApiEmail` |

`CloudflareClientOptions.AuthenticationMode` resolves which one you have configured; if an API token is present it wins. Setting a global key without the email is a configuration error rather than a silent 401, and `options.Validate()` reports every problem at once.

Environment variables, read by `CreateFromEnvironment()`, `AddCloudflareFromEnvironment()` and the integration tests alike:

| Variable | Purpose |
| --- | --- |
| `CLOUDFLARE_API_TOKEN` | API token |
| `CLOUDFLARE_GLOBAL_API_KEY` | legacy global key |
| `CLOUDFLARE_API_EMAIL` | account email, paired with the global key |
| `CLOUDFLARE_ACCOUNT_ID` | default account for Workers, Pages, D1, KV, R2 |
| `CLOUDFLARE_ZONE_ID` | default zone for DNS records and cache purge |
| `CLOUDFLARE_BASE_ADDRESS` | override the endpoint, e.g. for a recording proxy |

---

## Dependency injection

```csharp
using CFNet.DependencyInjection;

builder.Services.AddCloudflare(options =>
{
    options.ApiToken  = builder.Configuration["Cloudflare:ApiToken"];
    options.AccountId = builder.Configuration["Cloudflare:AccountId"];
});
```

| Extension | When to use it |
| --- | --- |
| `AddCloudflare(Action<CloudflareClientOptions>?)` | configure in code |
| `AddCloudflare(CloudflareClientOptions)` | hand over an options instance |
| `AddCloudflareFromConfiguration(IConfiguration, sectionName = "Cloudflare", configure?)` | bind `appsettings.json` |
| `AddCloudflareFromEnvironment(configure?)` | read the `CLOUDFLARE_*` variables |
| `AddCloudflareHttpClient(configure?, httpClientName?, configureClient?)` | route through `IHttpClientFactory` |
| `AddCloudflareClientFactory(configure?)` | several clients over shared defaults |

Any of these registers `ICloudflareClient`, `CloudflareClient`, `ICloudflareTransport`, `ICloudflareClientFactory` and all nine service interfaces, so you can inject exactly the surface you need:

```csharp
public sealed class DnsProvisioner(IDnsRecordsService dns, IZonesService zones)
{
    public Task<DnsRecord> PointAtAsync(string host, string address) =>
        dns.CreateAddressRecordAsync(host, address, proxied: true);
}
```

`appsettings.json`:

```json
{
  "Cloudflare": {
    "ApiToken": "",
    "AccountId": "",
    "ZoneId": "",
    "Timeout": "00:00:30",
    "MaxRetries": 2,
    "ThrowOnApiError": true
  }
}
```

Registrations use `TryAdd*`, so registering your own `ICloudflareTransport` **before** calling `AddCloudflare` wins — that is the intended hook for logging, caching, metrics or a mock in tests.

---

## What is covered

Phase 1 covers six product areas. See [docs/api-coverage.md](docs/api-coverage.md) for the endpoint-by-endpoint list.

**Domains (Zones + DNS)** — list/get/create/update/delete zones, `ListAllAsync` across pages, activation checks, cache purge (everything, files, tags, hosts, prefixes, ignore-tags), DNS records with typed `A`/`AAAA`/`CNAME`/`TXT`/`MX`/`NS`/`SRV`/`CAA`/`LOC` data, `FindAsync`, PATCH and PUT semantics.

**Workers** — list/search scripts, get script metadata, download module content (base64 decoded for you), read `script-settings`, multipart upload with any mix of modules and bindings, delete, gradual deployments with percentage validation, account and per-script `workers.dev` subdomain.

**Pages** — projects (list/get/create/update/delete), deployments (list/get/create/delete), custom domains (list/add/verify/delete), build and deployment configuration models with `[JsonExtensionData]` so unknown fields survive a round trip.

**D1** — databases (list/get/get-by-name/create/delete), single-statement `QueryAsync`, transactional `BatchAsync`, `D1QueryResult` with `Rows<T>()` and `FirstColumn<T>()`, timings and `served_by_*` metadata, backups (list/get/restore).

**KV** — namespaces (list/get/get-by-title/create/rename/delete), key listings with `cursor` + `limit`, values as text/bytes/JSON, per-key metadata from the `CF-KV-Metadata` header, bulk writes, TTL and absolute-expiration validation.

**R2** — buckets (list/get/create/update/delete), location hints, storage classes, jurisdiction-restricted buckets via the `cf-r2-jurisdiction` header, and the `{ buckets: [...] }` result wrapper unwrapped into a normal page.

---

## Design notes

**Strongly typed, but not brittle.** Cloudflare adds fields constantly, so every model carries `[JsonExtensionData] IDictionary<string, JsonElement>? AdditionalData` and unknown JSON is preserved instead of discarded. Unknown enum-like values are modelled as `string` plus a constants class (`ZoneStatusValues`, `DnsRecordTypes`, `WorkerBindingTypes`, `PagesDeploymentStatusValues`, …) rather than as enums, because a new Cloudflare value must not become a deserialization failure. Real enums are used only where the set is closed and the library must serialize them (`SortDirection`, `R2Jurisdiction`, `R2StorageClass`, `R2LocationHint`, `CloudflareAuthenticationMode`).

**One converter for the wire's inconsistent casing.** Cloudflare mixes `snake_case` (`kv_namespace`), `camelCase` (`locationHint`) and `PascalCase` (`Standard`) — sometimes in the same payload. .NET 8's `JsonStringEnumConverter` cannot express per-member wire names, so the library ships a small `CloudflareValueAttribute` + `CloudflareEnumConverter<T>` pair and registers no global enum converter.

**Percent-encoding is the library's job.** KV keys, Worker script names, D1 database ids and R2 bucket names all end up in a URL path. `CloudflareUrl.EncodeSegment` escapes them, so a key like `users/1/profile` stays data and does not become two path segments.

**`%2F` survives.** `new Uri(string)` canonicalizes and unescapes `%2F`, which would silently change the request. Where it matters the library builds the `Uri` with `UriCreationOptions.DangerousPathAndQueryCanonicalization` disabled.

**Requests are values you can inspect.** `CloudflareRequest` describes method, path, query, headers and body without sending anything, and `ICloudflareTransport` is the single seam to the network. That is why the unit tests need no network and why the gallery can render every call: it just decorates the transport.

---

## Errors, retries and pagination

```csharp
using CFNet.Exceptions;

try
{
    await client.Zones.GetAsync(zoneId);
}
catch (CloudflareApiException ex) when (ex.IsNotFound)
{
    // 404 - the zone is gone. ex.ErrorCode, ex.Errors, ex.RayId are all populated.
}
catch (CloudflareApiException ex) when (ex.IsRateLimited)
{
    // 429 - retry-after was already honoured by the transport if retries were enabled.
}
```

| Exception | Raised when |
| --- | --- |
| `CloudflareApiException` | Cloudflare answered `success: false` or a non-2xx status |
| `CloudflareTransportException` | the request never completed (DNS, TLS, timeout, cancellation) |
| `CloudflareConfigurationException` | options or a request body are invalid — reported **before** anything is sent |

Set `options.ThrowOnApiError = false` to receive the envelope instead and inspect `response.Errors` yourself. Individual calls can also tolerate specific statuses: `GetAsync` on a zone, DNS record, D1 database, KV namespace, R2 bucket, Worker, Pages project or backup returns `null` on 404 rather than throwing.

Retries cover **408, 429 and 5xx only**. A `Retry-After` header wins; otherwise the delay is `RetryBaseDelay * 2^attempt` plus up to 25% jitter when `UseRetryJitter` is on, clamped to `MaxRetryDelay`.

Pagination comes in both Cloudflare flavours and both are handled:

```csharp
// Page based (zones, DNS, D1, KV namespaces, Pages)
var page = await client.Zones.ListAsync(new ZoneListParameters { Page = 2, PerPage = 50 });
Console.WriteLine($"{page.Count} of {page.TotalCount}, {page.TotalPages} pages");

// Cursor based (Workers scripts, R2 buckets, KV keys)
await foreach (var bucket in client.R2.ListAllBucketsAsync())
{
    Console.WriteLine(bucket.Name);
}
```

Every service that lists also exposes a `ListAll…Async` returning `IAsyncEnumerable<T>` that walks pages or cursors for you, with a guard against a cursor that never advances.

---

## Testing

```bash
dotnet test tests/CF.Net.UnitTests --filter "Category!=Integration"
```

The unit suite is **fully offline**. A `StubHttpMessageHandler` records every `HttpRequestMessage` and replays canned envelopes; `CloudflareHarness` wraps it so a test reads like this:

```csharp
using var harness = CloudflareHarness.Create();
harness.RespondWithResult("{\"id\":\"zone-1\",\"name\":\"example.com\",\"status\":\"active\"}");

var zone = await harness.Client.Zones.GetAsync("zone-1");

Assert.Equal("example.com", zone!.Name);
Assert.Equal("/client/v4/zones/zone-1", harness.LastRequest.Path);
Assert.Equal("Bearer test-token", harness.LastRequest.Header("Authorization"));
```

That covers URL construction, query-string ordering and escaping, header selection, envelope parsing, error mapping, retry/backoff arithmetic, multipart upload layout, base64 decoding, cursor loops and every serializer quirk — with no network access, in milliseconds.

Both TFMs are exercised: `net8.0` and `net10.0`.

Three layers sit behind that one command:

| Layer | Where | Network |
| --- | --- | --- |
| Unit tests | `tests/CF.Net.UnitTests` | none — `StubHttpMessageHandler` answers every call |
| Integration tests | `tests/CF.Net.IntegrationTests` | the real `api.cloudflare.com`, and only when credentials are present |
| Cassettes | `tests/CF.Net.TestKit` + `tests/cassettes` | recorded once against the real API, replayed offline forever after |

The cassette layer is the bridge between the other two. Record one genuine run, commit the redacted
transcript, and from then on every push and pull request replays it two ways: `CassetteTests` binds
**real Cloudflare payloads** to the typed models, and *Replay committed cassettes (offline)* in
`ci.yml` runs the **real integration suite** against the recording — real service code, real URLs, no
credentials, no network, no quota. Replay runs over a handler that throws if anything tries to escape,
so a missing recording fails loudly with `CassetteMissException` instead of quietly going online. See
[docs/integration-testing.md](docs/integration-testing.md#6-recording-a-cassette).

---

## The Avalonia gallery

`samples/CF.Net.Gallery` is a single-window desktop app for exercising the API by hand.

```bash
dotnet run --project samples/CF.Net.Gallery
```

- **Left-hand navigation**, one page per module: Domains, Workers, Pages, D1, KV, R2 — plus a Credentials page.
- **Credentials page** takes an API token or the legacy global key + email, the default account and zone ids, base address, timeout, retries and `ThrowOnApiError`. Values are pre-filled from the `CLOUDFLARE_*` environment variables and can optionally be saved to `%APPDATA%/CF.Net.Gallery/credentials.json` (plain text — opt-in, and off by default).
- **Every page** lists the real operations with the real endpoint in its heading, and each button runs the corresponding library call.
- **The inspector at the bottom** shows, live: the outgoing request (method, path, query, per-request headers, JSON or multipart body), the response (status, timing, `CF-RAY`, headers, pretty-printed body, `errors`, `messages`), the **typed result** re-serialized so you can see what the models captured, and a session history.
- The gallery never renders `Authorization`, `X-Auth-Key` or `X-Auth-Email`, so a screenshot or a screen share cannot leak a credential.

It works by decorating the transport — `RecordingTransport : ICloudflareTransport` — which is also the clearest demonstration of the extension point the library is built around.

Avalonia 12 is used because it is the first Avalonia line that supports the .NET 10 SDK this repository builds with; the gallery itself targets net8.0, which Avalonia 12 still supports.

---

## Integration tests in GitHub Actions

The tests that hit the real API live in `tests/CF.Net.IntegrationTests`, are tagged `[Trait("Category", "Integration")]`, and **skip themselves** when the credentials are missing — so a plain `dotnet test` never touches your account.

### 1. Create a scoped API token

Cloudflare dashboard → **My Profile** → **API Tokens** → **Create Token** → *Custom token*:

| Permission | Access |
| --- | --- |
| Zone | DNS — Edit |
| Zone | Zone — Read |
| Account | Workers Scripts — Edit |
| Account | Pages — Edit |
| Account | D1 — Edit |
| Account | Workers KV Storage — Edit |
| Account | Workers R2 Storage — Edit |
| User | API Tokens — Read |

Add a **Zone resource** for the specific zone you want to test against, and an **Account resource** for the account. Never use the Global API Key in CI: it is account-wide and cannot be scoped.

### 2. Add repository secrets

GitHub → your repository → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**:

| Secret | Required | What to put in it |
| --- | --- | --- |
| `CLOUDFLARE_API_TOKEN` | yes | the token secret from step 1, shown **once** after *Create Token* — `cfut_…` (yours) or `cfat_…` (the account's); pre-2026 tokens are 40 unprefixed characters |
| `CLOUDFLARE_ACCOUNT_ID` | yes | 32 hexadecimal characters — any domain's **Overview** → **API** section, or `Ctrl`/`Cmd`+`K` → *Copy account ID* |
| `CLOUDFLARE_ZONE_ID` | yes | 32 hexadecimal characters — that domain's **Overview** → **API** section → **Zone ID** |
| `CLOUDFLARE_API_EMAIL` | no | the address the Cloudflare account logs in with (legacy path only) |
| `CLOUDFLARE_GLOBAL_API_KEY` | no | **My Profile** → **API Tokens** → the **API Keys** tab → *View* — `cfk_…`, or a pre-2026 37-45 character hex key |

Both ids can also be read back through the API (`GET /accounts`, `GET /zones`) when the token is
allowed to list them; the dashboard always works. Neither is an account *name* and neither is a
dashboard URL. A value with the wrong shape is reported by name — in the test output and in the CI
summary — instead of surfacing as twenty authentication failures.

Secrets are injected into the workflow as environment variables, which is exactly what `CloudflareClientOptions.FromEnvironment()` and the test fixture read:

```yaml
env:
  CLOUDFLARE_API_TOKEN: ${{ secrets.CLOUDFLARE_API_TOKEN }}
  CLOUDFLARE_ACCOUNT_ID: ${{ secrets.CLOUDFLARE_ACCOUNT_ID }}
  CLOUDFLARE_ZONE_ID: ${{ secrets.CLOUDFLARE_ZONE_ID }}
```

GitHub masks secret values in every log line automatically, and the tests never print them.

### 3. Run it

`.github/workflows/integration.yml` runs on **manual dispatch** and on a **Monday 04:00 UTC schedule** — never on push or pull request, so forks and drive-by commits cannot spend your quota or touch your account.

```
Actions → Integration → Run workflow → [ ] Allow tests that create and delete resources
```

The workflow first reports which credential scheme it found. With none, it emits a warning and finishes green, because there is nothing to verify.

With credentials it verifies them against the live API **before building anything** — one `curl` to `/user/tokens/verify` (or `/user` for the legacy pair) — so an expired, revoked or under-scoped secret fails the job with Cloudflare's own error code and message instead of turning 25 tests red. The **Run workflow** form also takes an optional `filter` (`FullyQualifiedName~R2`, appended to `Category=Integration&`) to narrow a run, and a `cassette` name to record one.

### What a run reports back

The job appends a summary with three parts:

- an **outcome table** parsed from the `.trx`, plus the message of every failing test;
- a **skip table** — xunit v2 cannot skip a test dynamically, so a test with nothing to do ends early and is reported as *passed*. Every early exit is recorded with its reason, which is how you tell a green no-op from a green run that really talked to Cloudflare;
- a **call transcript** — method, path, query and status for every request the run made, so you can see exactly what touched your account, and that a write run deleted what it created. Credentials live in headers, so none of it can reach the report.

### What the tests do

| Suite | Runs when | Behaviour |
| --- | --- | --- |
| `ReadOnlyIntegrationTests` | credentials present | verify token, read the user, list accounts/zones/DNS/Workers/Pages/D1/KV/R2, walk every zone page, run `SELECT 1`, assert 404 handling for unknown ids |
| `WriteIntegrationTests` | credentials **and** `CLOUDFLARE_IT_ENABLE_WRITES=true` | full create → read → update → delete lifecycles for a DNS TXT record, a KV namespace and value, an R2 bucket, a D1 database, a Worker script and a Pages project, plus a cache purge by URL. Every one cleans up in a `finally` block. |

### Running them locally

```bash
export CLOUDFLARE_API_TOKEN=...
export CLOUDFLARE_ACCOUNT_ID=...
export CLOUDFLARE_ZONE_ID=...
export CLOUDFLARE_IT_ENABLE_WRITES=true          # only if you want the lifecycle tests

dotnet test tests/CF.Net.IntegrationTests --filter "Category=Integration" --logger "console;verbosity=detailed"
```

Or with [dotnet user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) exported into the shell — anything but committing a token. See [docs/integration-testing.md](docs/integration-testing.md) for the full write-up, including how to point the tests at a recording proxy.

---

## Repository layout

```
CF.Net.sln
Directory.Build.props          shared build properties (LangVersion, Nullable, analyzers, version)
global.json                    SDK floor, rolls forward to the newest installed major
.github/
  workflows/ci.yml             push + PR: restore, build, unit tests on net8.0 and net10.0, pack, gallery
  workflows/integration.yml    dispatch + weekly schedule: real API, secret-gated
  dependabot.yml
docs/
  getting-started.md
  integration-testing.md
  api-coverage.md
src/CF.Net/                    the library (net8.0;net10.0, namespace CFNet, assembly CF.Net)
  Configuration/  Exceptions/  Serialization/  Http/  Services/  DependencyInjection/
  Models/{Common,Zones,Dns,Workers,Pages,D1,Kv,R2}/
tests/CF.Net.UnitTests/        offline, both TFMs
tests/CF.Net.IntegrationTests/ real API, Category=Integration, net8.0
samples/CF.Net.Gallery/        Avalonia desktop gallery, net8.0
```

---

## License

MIT — see [LICENSE](LICENSE).
