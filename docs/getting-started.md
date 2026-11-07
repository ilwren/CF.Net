# Getting started

This guide takes you from an empty project to listing your zones, writing a DNS record and querying a
D1 database.

## 1. Add the package

```bash
dotnet new console -n CloudflareDemo
cd CloudflareDemo
dotnet add package CF.Net
```

The package targets `net8.0` and `net10.0`. Its only direct dependencies are
`Microsoft.Extensions.Http` 8.0.1 and `Microsoft.Extensions.Options.ConfigurationExtensions` 8.0.0,
so it drops into an ASP.NET Core 8/9/10 app, a Worker service, a console tool or a MAUI/Avalonia
client without version conflicts.

## 2. Get credentials

Cloudflare dashboard → **My Profile** → **API Tokens** → **Create Token**. Start from the
*Edit zone DNS* template for a first test, or build a custom token:

- **Permissions**: `Zone → DNS → Edit`, `Zone → Zone → Read`
- **Zone resources**: *Include → Specific zone → your domain*

Copy the token. It is shown once.

Then note two identifiers:

- **Account ID** — dashboard → any account → right-hand sidebar
- **Zone ID** — dashboard → the zone → right-hand sidebar

Never use the Global API Key for new code. It is account-wide and cannot be scoped. CF.Net supports
it because plenty of existing systems still rely on it, not because it is a good idea.

## 3. Configure

Pick whichever fits how your application is built.

### Environment variables

```bash
export CLOUDFLARE_API_TOKEN='v1.0-abc...'
export CLOUDFLARE_ACCOUNT_ID='023e105f4ecef8ad9ca31a8372d0c353'
export CLOUDFLARE_ZONE_ID='372e67954025e0ba6aaa6d586b9e0b59'
```

```csharp
using CFNet;

using var client = CloudflareClient.CreateFromEnvironment();
```

On Windows use `dotnet user-secrets` or the system environment instead of exporting in a shell
profile that gets committed.

### In code

```csharp
using CFNet;

using var client = CloudflareClient.Create(options =>
{
    options.ApiToken  = apiToken;
    options.AccountId = accountId;
    options.ZoneId    = zoneId;

    options.Timeout         = TimeSpan.FromSeconds(30);
    options.MaxRetries      = 3;
    options.UseRetryJitter  = true;
    options.ThrowOnApiError = true;
});
```

### With dependency injection

```csharp
using CFNet.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCloudflareFromConfiguration(builder.Configuration);
// or: builder.Services.AddCloudflareFromEnvironment();
// or: builder.Services.AddCloudflare(o => { o.ApiToken = ...; });

builder.Services.AddScoped<DnsProvisioner>();
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

Keep the token out of source control: put it in user-secrets locally, in Key Vault / SSM / an
environment variable in production, and bind it at startup.

## 4. Check that it works

```csharp
var check = await client.CheckConnectionAsync();

Console.WriteLine(check.IsSuccess ? check.Message : $"FAILED: {check.Message}");
Console.WriteLine($"scheme={(int?)check.StatusCode} ray={check.Elapsed.TotalMilliseconds:F0}ms");
```

`CheckConnectionAsync` **never throws**. It verifies the token (or reads `/user` for a global key)
and reports the outcome, including `IsTimeout` and `ErrorCode`, which makes it a good startup probe
and a good health-check endpoint.

## 5. Domains and DNS

```csharp
using CFNet.Models.Dns;
using CFNet.Models.Zones;

// Every zone, walked page by page.
await foreach (var zone in client.Zones.ListAllAsync())
{
    Console.WriteLine($"{zone.Name,-30} [{zone.Status}] plan={zone.Plan?.Name}");
}

// One zone by id.
var zone = await client.Zones.GetAsync();          // uses the default ZoneId
Console.WriteLine(string.Join(", ", zone?.NameServers ?? []));

// Filtered, single page.
var pending = await client.Zones.ListAsync(new ZoneListParameters
{
    Status    = ZoneStatusValues.Pending,
    PerPage   = 50,
    Direction = SortDirection.Ascending,
    Order     = "name",
});

// Create an A record.
var a = await client.DnsRecords.CreateAddressRecordAsync(
    "www.example.com", "192.0.2.1", proxied: true, ttl: 1);

// A TXT record with a comment.
var txt = await client.DnsRecords.CreateAsync(new DnsRecord
{
    Type    = DnsRecordTypes.Txt,
    Name    = "example.com",
    Content = "v=spf1 -all",
    Ttl     = 3600,
    Comment = "hard fail",
    Tags    = ["spf"],
});

// Everything named "www", any type.
var www = await client.DnsRecords.FindAsync("www.example.com");

// PATCH one field, or PUT the whole record.
await client.DnsRecords.UpdateAsync(txt.Id!, new DnsRecord { Content = "v=spf1 ~all" });

await client.DnsRecords.DeleteAsync(txt.Id!);
```

Structured record types are typed too:

```csharp
var srv = await client.DnsRecords.CreateAsync(new DnsRecord
{
    Type = DnsRecordTypes.Srv,
    Name = "example.com",
    Data = new DnsRecordSrvData
    {
        Service  = "_sip",
        Proto    = "_tcp",
        Priority = 10,
        Weight   = 60,
        Port     = 5060,
        Target   = "sip.example.com",
    },
});
```

### Purging the cache

```csharp
await client.Zones.PurgeCacheAsync(PurgeCacheRequest.Everything());

await client.Zones.PurgeCacheAsync(PurgeCacheRequest.Files(new[]
{
    "https://example.com/assets/app.css",
    "https://example.com/assets/app.js",
}));

// Enterprise: by tag, optionally excluding some.
await client.Zones.PurgeCacheAsync(new PurgeCacheRequest
{
    Tags       = ["blog", "assets"],
    IgnoreTags = ["never-purge"],
    Hosts      = ["example.com"],
});
```

`PurgeCacheRequest.EnsureValid()` runs before anything is sent, so an empty request fails locally
rather than with a confusing API error.

## 6. Workers

```csharp
using CFNet.Models.Workers;

await foreach (var script in client.Workers.ListAllAsync())
{
    Console.WriteLine($"{script.ScriptName} modified={script.ModifiedOn:u}");
}

// Upload a module worker. Sent as multipart/form-data: a JSON "metadata" part plus one
// file part per module.
var uploaded = await client.Workers.UploadAsync(
    "hello-cfnet",
    """
    export default {
      async fetch(request, env) {
        return new Response(`hello from ${env.GREETING}`);
      },
    };
    """,
    compatibilityDate: "2025-01-01",
    bindings:
    [
        WorkerBinding.PlainText("GREETING", "CF.Net"),
        WorkerBinding.KvNamespace("CACHE", kvNamespaceId),
        WorkerBinding.D1Database("DB", databaseId),
        WorkerBinding.R2Bucket("ASSETS", "my-bucket"),
    ]);

// Read back the modules; base64 is decoded for you.
var content = await client.Workers.GetContentAsync("hello-cfnet");
Console.WriteLine(content!.MainModule);

// Gradual deployment. Percentages must total exactly 100.
await client.Workers.CreateDeploymentAsync("hello-cfnet",
    CreateWorkerDeploymentRequest.SingleVersion(versionId));

await client.Workers.DeleteAsync("hello-cfnet");
```

For anything more elaborate, build the request object and let `Validate()` check it before you send:

```csharp
var request = new UploadWorkerScriptRequest("hello-cfnet", source, "2025-01-01")
{
    Modules = [new WorkerModule("helper.mjs", helperSource, WorkerModuleContentTypes.JavaScriptModule)],
    Bindings = [WorkerBinding.SecretText("API_KEY", secret)],
    CompatibilityFlags = ["nodejs_compat"],
    Logpush = true,
    Tags = ["demo"],
};

request.Validate();
await client.Workers.UploadAsync(request);
```

## 7. Pages

```csharp
using CFNet.Models.Pages;

var project = await client.Pages.CreateAsync("my-site", productionBranch: "main");

await client.Pages.UpdateAsync("my-site", new UpdatePagesProjectRequest
{
    ProductionBranch = "develop",
});

var deployments = await client.Pages.ListDeploymentsAsync("my-site",
    new PagesDeploymentListParameters { Branch = "main", PerPage = 20 });

await client.Pages.AddDomainAsync("my-site", "www.example.com");

await client.Pages.DeleteAsync("my-site");
```

Pages models bindings and environment variables as **dictionaries keyed by name**, unlike Workers'
array — `PagesDeploymentConfigs` reflects that. Unknown fields are kept in
`AdditionalData`, so reading a project, changing one thing and writing it back does not silently
drop configuration Cloudflare added after this library was built.

## 8. D1

```csharp
using CFNet.Models.D1;

var database = await client.D1.CreateAsync("demo", primaryLocationHint: "weur");

await client.D1.BatchAsync(database.Uuid!,
[
    new D1Query("CREATE TABLE items (id INTEGER PRIMARY KEY, label TEXT)"),
    new D1Query("INSERT INTO items (label) VALUES (?), (?)", ["alpha", "beta"]),
]);

var result = await client.D1.QueryAsync(
    database.Uuid!,
    "SELECT id, label FROM items WHERE id > ? ORDER BY id LIMIT ?",
    [0, 10]);

foreach (var item in result.Rows<Item>())
{
    Console.WriteLine($"{item.Id}: {item.Label}");
}

Console.WriteLine($"read={result.Meta?.RowsRead} took={result.Meta?.Duration}ms colo={result.Meta?.ServedByColo}");

record Item(int Id, string Label);
```

Two things that trip people up, handled for you:

- the collection path is `/d1/database` (singular), and the filter parameter is `name`, not `search`;
- the query body is `{ sql, params }` or `{ batch: [...] }` — there is **no** `action` field.

`GetByNameAsync` exists even though the API has no lookup-by-name endpoint: it lists and filters
locally.

## 9. KV

```csharp
using CFNet.Models.Kv;

var ns = await client.Kv.CreateNamespaceAsync("sessions");

// Values are the raw HTTP body, never a JSON envelope.
await client.Kv.PutStringAsync(ns.Id!, "users/1/profile", """{"role":"admin"}""", expirationTtl: 3600);

var value = await client.Kv.GetValueAsync(ns.Id!, "users/1/profile");
if (value.Found)
{
    Console.WriteLine(value.Text);
    Console.WriteLine(value.GetMetadata<Dictionary<string, object>>());   // from CF-KV-Metadata
}

// Keys are paginated with cursor + limit, and slashes inside a key are percent-encoded.
await foreach (var key in client.Kv.ListAllKeysAsync(ns.Id!, prefix: "users/"))
{
    Console.WriteLine(key.Name);
}

await client.Kv.BulkWriteAsync(ns.Id!,
[
    KvBulkWriteOperation.Put("a", "1"),
    KvBulkWriteOperation.PutBytes("logo", pngBytes),
    KvBulkWriteOperation.Delete("stale"),
]);

await client.Kv.DeleteNamespaceAsync(ns.Id!);
```

`GetStringAsync` and `GetJsonAsync<T>` return `null` for a missing key instead of throwing, and
`PutValueAsync` rejects a TTL under 60 seconds or an expiration timestamp already in the past
before the request leaves the process.

## 10. R2

```csharp
using CFNet.Models.R2;

var bucket = await client.R2.CreateBucketAsync(
    "assets",
    locationHint: R2LocationHint.Weur,
    storageClass: R2StorageClass.Standard);

// EU-restricted buckets need the cf-r2-jurisdiction header on every call.
var euBucket = await client.R2.GetBucketAsync("eu-assets", R2Jurisdiction.Eu);

await foreach (var existing in client.R2.ListAllBucketsAsync())
{
    Console.WriteLine($"{existing.Name} created={existing.CreationDate:u} class={existing.StorageClass}");
}

await client.R2.UpdateBucketAsync("assets", R2StorageClass.InfrequentAccess);
await client.R2.DeleteBucketAsync("assets");
```

R2's list endpoint returns `{ "buckets": [...] }` rather than a bare array; the library unwraps it so
you get the same `CloudflarePage<R2Bucket>` as everywhere else. Bucket names are validated locally
(3–63 characters, lowercase letters/digits/single hyphens, must start and end alphanumeric, no
`xn--` prefix).

Changing a bucket's default storage class is a `PATCH` that carries the class in the
`cf-r2-storage-class` header — that endpoint takes no request body, which is why
`UpdateBucketAsync` accepts the class as an argument rather than a request object.

Everything the v4 API does not cover lives on R2's S3-compatible endpoint, bucket CORS rules
(`PutBucketCors` / `GetBucketCors`) above all. `CloudflareDefaults.R2.S3Endpoint(accountId)` builds
`https://{account_id}.r2.cloudflarestorage.com` for you to point an S3 SDK or the AWS CLI at; it
authenticates with AWS Signature Version 4 and the R2 access keys from the dashboard, not with your
Cloudflare API token, so CF.Net builds the URL but does not sign requests for it.

## 11. Handling failures

```csharp
using CFNet.Exceptions;

try
{
    await client.Zones.CreateAsync("example.com");
}
catch (CloudflareConfigurationException ex)
{
    // Nothing was sent. ex.Problems lists every issue found.
    foreach (var problem in ex.Problems) Console.WriteLine(problem);
}
catch (CloudflareApiException ex) when (ex.IsRateLimited)
{
    Console.WriteLine($"429 - CF-RAY {ex.RayId}");
}
catch (CloudflareApiException ex)
{
    Console.WriteLine($"{(int?)ex.StatusCode} code={ex.ErrorCode}: {ex.Message}");
    foreach (var error in ex.Errors) Console.WriteLine($"  {error}");
}
catch (CloudflareTransportException ex)
{
    Console.WriteLine(ex.IsTimeout ? "timed out" : "network failure");
}
```

Prefer inspecting to catching? Turn throwing off:

```csharp
options.ThrowOnApiError = false;

var response = await client.Transport.SendAsync<List<Zone>>(CloudflareRequest.Get("zones"));
if (!response.Success)
{
    Console.WriteLine(string.Join("; ", response.Errors));
}
```

## Where to go next

- [docs/api-coverage.md](api-coverage.md) — every endpoint this release wraps
- [docs/integration-testing.md](integration-testing.md) — running the real-API suite locally and in CI
- `samples/CF.Net.Gallery` — a desktop app to click through all of the above
