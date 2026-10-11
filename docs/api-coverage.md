# API coverage

Every Cloudflare API v4 endpoint this release wraps, with the CF.Net method that calls it.
Base URL: `https://api.cloudflare.com/client/v4`.

`{account_id}` and `{zone_id}` fall back to `CloudflareClientOptions.AccountId` /
`.ZoneId` when the corresponding argument is left `null`, and raise a
`CloudflareConfigurationException` when neither is available.

Status legend: ✅ wrapped · ⛔ deliberately out of scope for this phase.

---

## Zones (Domains)

`IZonesService` — `client.Zones`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(ZoneListParameters?)` | `GET /zones` | `page`, `per_page`, `name`, `status`, `account.id`, `order`, `direction`, `match` |
| `ListAllAsync(ZoneListParameters?)` | `GET /zones` | `IAsyncEnumerable<Zone>`, walks every page |
| `GetAsync(string? zoneId)` | `GET /zones/{zone_id}` | returns `null` on 404 |
| `GetByNameAsync(string name)` | `GET /zones?name=` | filters client-side for an exact match |
| `CreateAsync(string name, string type)` | `POST /zones` | `type` ∈ `full`, `partial`, `secondary`, `internal` |
| `CreateAsync(CreateZoneRequest)` | `POST /zones` | |
| `UpdateAsync(string? zoneId, UpdateZoneRequest)` | `PATCH /zones/{zone_id}` | `paused`, `plan`, `type`, `vanity_name_servers` |
| `SetPausedAsync(bool paused, string? zoneId)` | `PATCH /zones/{zone_id}` | convenience wrapper |
| `DeleteAsync(string zoneId)` | `DELETE /zones/{zone_id}` | |
| `RunActivationCheckAsync(string? zoneId)` | `POST /zones/{zone_id}/activation_check` | |
| `PurgeCacheAsync(PurgeCacheRequest, string? zoneId)` | `POST /zones/{zone_id}/purge_cache` | `purge_everything`, `files`, `tags`, `hosts`, `prefixes`, `ignore_tags` |

⛔ Zone settings (`/zones/{id}/settings/*`), SSL, firewall rules, page rules, rate limits, WAF,
Load balancing, Waiting Room, Turnstile — separate phases.

## DNS records

`IDnsRecordsService` — `client.DnsRecords`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(DnsRecordListParameters?, string? zoneId)` | `GET /zones/{zone_id}/dns_records` | `type`, `name`, `content`, `order`, `direction`, `proxied`, `tag` (repeatable), `tag_match`, `match`, `search` |
| `ListAllAsync(...)` | `GET /zones/{zone_id}/dns_records` | walks every page |
| `FindAsync(string name, string? type, string? zoneId)` | `GET /zones/{zone_id}/dns_records?name=` | exact-name filter client-side |
| `GetAsync(string recordId, string? zoneId)` | `GET /zones/{zone_id}/dns_records/{record_id}` | returns `null` on 404 |
| `CreateAsync(DnsRecord, string? zoneId)` | `POST /zones/{zone_id}/dns_records` | validates the record first |
| `CreateAddressRecordAsync(name, content, proxied, ttl, zoneId)` | `POST …/dns_records` | `A` or `AAAA`, chosen from the address |
| `CreateCnameRecordAsync(name, target, proxied, ttl, zoneId)` | `POST …/dns_records` | |
| `CreateTxtRecordAsync(name, content, ttl, zoneId)` | `POST …/dns_records` | always unproxied |
| `UpdateAsync(recordId, DnsRecord, zoneId)` | `PATCH /zones/{zone_id}/dns_records/{record_id}` | sends only the fields you set |
| `ReplaceAsync(recordId, DnsRecord, zoneId)` | `PUT /zones/{zone_id}/dns_records/{record_id}` | full replacement |
| `DeleteAsync(recordId, zoneId)` | `DELETE /zones/{zone_id}/dns_records/{record_id}` | |
| `ExportAsync(zoneId)` | `GET /zones/{zone_id}/dns_records/export` | returns the BIND zone file as text, not an envelope |
| `ImportAsync(bindContents, proxied, zoneId)` | `POST /zones/{zone_id}/dns_records/import` | `multipart/form-data`: a `file` part plus an optional plain `proxied` field; returns `DnsRecordsImportResult` |

Record types modelled with constants and typed data: `A`, `AAAA`, `CAA`, `CERT`, `CNAME`, `DNSKEY`,
`DS`, `HTTPS`, `LOC`, `MX`, `NAPTR`, `NS`, `PTR`, `SMIMEA`, `SRV`, `SSHFP`, `SVCB`, `TLSA`, `TXT`,
`URI`. Structured payloads are `DnsRecordSrvData`, `DnsRecordLocData`, `DnsRecordCaaData`,
`DnsRecordSshfpData`, `DnsRecordDnskeyData`, `DnsRecordTlsaData`, `DnsRecordUriData`, … reachable
through `DnsRecord.Data` and `DnsRecord.GetData<T>()`.

⛔ DNS analytics, DNSSEC, custom nameservers, secondary zone transfers.

## Users and accounts

`IUserService` — `client.User`; `IAccountsService` — `client.Accounts`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `User.VerifyTokenAsync()` | `GET /user/tokens/verify` | |
| `User.GetAsync()` | `GET /user` | |
| `User.CheckConnectionAsync()` *(also on `ICloudflareClient`)* | `GET /user/tokens/verify` or `GET /user` | never throws; reports scheme, status, `ErrorCode`, `IsTimeout`, elapsed |
| `Accounts.ListAsync(AccountListParameters?)` | `GET /accounts` | `page`, `per_page`, `name` |
| `Accounts.GetAsync(string? accountId)` | `GET /accounts/{account_id}` | returns `null` on 403/404 |

## Workers

`IWorkersScriptsService` — `client.Workers`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(WorkerScriptListParameters?, string? accountId)` | `GET /accounts/{account_id}/workers/scripts` | cursor paginated: `cursor`, `per_page`, `name`, `order`, `direction` |
| `ListAllAsync(...)` | `GET …/workers/scripts` | walks the cursor, guarded against a cursor that never advances |
| `GetAsync(scriptName, accountId)` | `GET /accounts/{account_id}/workers/scripts/{script_name}` | `null` on 404 |
| `GetContentAsync(scriptName, accountId)` | `GET …/workers/scripts/{script_name}/content` | modules decoded from base64; `MainModule` exposed directly |
| `GetSettingsAsync(scriptName, accountId)` | `GET …/workers/scripts/{script_name}/script-settings` | **not** `/settings` — a common mistake |
| `UploadAsync(UploadWorkerScriptRequest, accountId)` | `PUT …/workers/scripts/{script_name}` | `multipart/form-data`: a JSON `metadata` part plus one file part per module |
| `UploadAsync(scriptName, source, compatibilityDate, bindings, accountId)` | `PUT …/workers/scripts/{script_name}` | convenience overload for a single-module worker |
| `DeleteAsync(scriptName, accountId)` | `DELETE …/workers/scripts/{script_name}` | |
| `ListDeploymentsAsync(scriptName, WorkerDeploymentListParameters?, accountId)` | `GET …/workers/scripts/{script_name}/deployments` | cursor paginated |
| `CreateDeploymentAsync(scriptName, CreateWorkerDeploymentRequest, accountId)` | `POST …/workers/scripts/{script_name}/deployments` | percentages must total exactly 100; `strategy` defaults to `percentage` |
| `GetSubdomainAsync(accountId)` | `GET /accounts/{account_id}/workers/subdomain` | |
| `UpdateSubdomainAsync(UpdateWorkerSubdomainRequest, accountId)` | `PATCH /accounts/{account_id}/workers/subdomain` | |
| `GetScriptSubdomainAsync(scriptName, accountId)` | `GET …/workers/scripts/{script_name}/subdomain` | |
| `UpdateScriptSubdomainAsync(scriptName, UpdateWorkerScriptSubdomainRequest, accountId)` | `POST …/workers/scripts/{script_name}/subdomain` | |

Binding types modelled: `kv_namespace`, `r2_bucket`, `plain_text`, `secret_text`, `service`,
`vpc_service`, `do_namespace`, `d1_database_binding`, `queue`, `analytics_engine`, `assets`,
`browser`, `vectorize`, `hyperdrive`, `ai`. Module content types: `application/javascript+module`,
`text/x-python`, `application/wasm`, `buffer`, `base64`, `text`.

⛔ The newer JSON-only flow (`POST /workers/workers` → `…/versions` → `…/deployments`), static asset
upload sessions, Workers for Platforms dispatch namespaces, cron triggers, custom domains, tails,
observability configuration, and Workers AI.

## Pages

`IPagesProjectsService` — `client.Pages`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(PagesProjectListParameters?, accountId)` | `GET /accounts/{account_id}/pages/projects` | `page`, `per_page`, `search` |
| `ListAllAsync(...)` | `GET …/pages/projects` | walks every page |
| `GetAsync(projectName, accountId)` | `GET …/pages/projects/{project_name}` | `null` on 404 |
| `CreateAsync(string projectName, string productionBranch, accountId)` | `POST …/pages/projects` | |
| `CreateAsync(CreatePagesProjectRequest, accountId)` | `POST …/pages/projects` | validates the project name locally (≤58 chars, lowercase/digit start, `[a-z0-9-]`) |
| `UpdateAsync(projectName, UpdatePagesProjectRequest, accountId)` | `PATCH …/pages/projects/{project_name}` | |
| `DeleteAsync(projectName, accountId)` | `DELETE …/pages/projects/{project_name}` | |
| `ListDeploymentsAsync(projectName, PagesDeploymentListParameters?, accountId)` | `GET …/pages/projects/{project_name}/deployments` | `page`, `per_page`, `branch`, `order`, `direction` |
| `GetDeploymentAsync(projectName, deploymentId, accountId)` | `GET …/deployments/{deployment_id}` | `null` on 404 |
| `CreateDeploymentAsync(projectName, CreatePagesDeploymentRequest?, accountId)` | `POST …/deployments` | direct-upload deployment |
| `DeleteDeploymentAsync(projectName, deploymentId, accountId)` | `DELETE …/deployments/{deployment_id}` | |
| `ListDomainsAsync(projectName, PagesDomainListParameters?, accountId)` | `GET …/pages/projects/{project_name}/domains` | |
| `AddDomainAsync(projectName, domainName, accountId)` | `POST …/domains` | |
| `UpdateDomainAsync(projectName, domainName, UpdatePagesDomainRequest, accountId)` | `PATCH …/domains/{domain}` | `verified`, `ownership_verified` |
| `DeleteDomainAsync(projectName, domainName, accountId)` | `DELETE …/domains/{domain}` | |

`PagesBuildConfig`, `PagesDeploymentConfigs` (`preview` + `production`, ~21 fields each),
`PagesProjectSource`, `PagesEnvVar` and the Pages binding dictionaries are modelled; anything
Cloudflare adds later is preserved in `AdditionalData`.

⛔ Pushing deployment assets (the S3-style `PUT` to the returned upload URL), build logs,
Functions routes, and the `pages/projects/{name}/bookmarks` family.

## D1

`ID1DatabaseService` — `client.D1`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(D1DatabaseListParameters?, accountId)` | `GET /accounts/{account_id}/d1/database` | **singular** path; filter is `name` (substring), not `search` |
| `ListAllAsync(...)` | `GET …/d1/database` | walks every page |
| `GetAsync(databaseId, accountId)` | `GET …/d1/database/{database_id}` | `null` on 404 |
| `GetByNameAsync(name, accountId)` | `GET …/d1/database` | lists and filters locally — there is no lookup-by-name endpoint |
| `CreateAsync(string name, string? primaryLocationHint, accountId)` | `POST …/d1/database` | |
| `CreateAsync(CreateD1DatabaseRequest, accountId)` | `POST …/d1/database` | |
| `DeleteAsync(databaseId, accountId)` | `DELETE …/d1/database/{database_id}` | |
| `QueryAsync(databaseId, sql, parameters, accountId)` | `POST …/d1/database/{database_id}/query` | body `{sql, params}` — there is **no** `action` field |
| `BatchAsync(databaseId, IEnumerable<D1Query>, accountId)` | `POST …/query` | body `{batch:[{sql,params}, …]}`, transactional |
| `ExecuteAsync(databaseId, D1QueryRequest, accountId)` | `POST …/query` | send a prebuilt request verbatim |
| `ListBackupsAsync(databaseId, D1BackupListParameters?, accountId)` | `GET …/d1/database/{database_id}/backup` | |
| `GetBackupAsync(databaseId, backupId, accountId)` | `GET …/backup/{backup_id}` | `null` on 404 |
| `RestoreBackupAsync(databaseId, backupId, accountId)` | `POST …/backup/{backup_id}/restore` | destructive |

`D1QueryResult` exposes `Results` (raw `JsonElement` rows), `Success`, `Meta` and the ergonomics
`Rows<T>()`, `FirstColumn<T>(name)` and `RowCount`. `D1QueryMeta` carries `changes`, `duration`,
`last_row_id`, `rows_read`, `rows_written`, `size_after`, `changed_db`, `served_by_colo`,
`served_by_primary`, `served_by_region` and `timings.sql_duration_ms`.

## KV

`IKvService` — `client.Kv`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListNamespacesAsync(KvNamespaceListParameters?, accountId)` | `GET /accounts/{account_id}/storage/kv/namespaces` | `page`, `per_page`, `search` |
| `ListAllNamespacesAsync(...)` | `GET …/namespaces` | walks every page |
| `GetNamespaceAsync(namespaceId, accountId)` | `GET …/namespaces/{namespace_id}` | `null` on 404 |
| `GetNamespaceByTitleAsync(title, accountId)` | `GET …/namespaces?search=` | `search` is a substring filter, so the match is re-checked exactly |
| `CreateNamespaceAsync(title, accountId)` | `POST …/namespaces` | body `{title}` |
| `RenameNamespaceAsync(namespaceId, title, accountId)` | `PUT …/namespaces/{namespace_id}` | |
| `DeleteNamespaceAsync(namespaceId, accountId)` | `DELETE …/namespaces/{namespace_id}` | |
| `ListKeysAsync(namespaceId, KvKeyListParameters?, accountId)` | `GET …/namespaces/{namespace_id}/keys` | `prefix`, **`limit`** (10–1000), `cursor` — not `per_page` |
| `ListAllKeysAsync(namespaceId, prefix, limit, accountId)` | `GET …/keys` | walks the cursor |
| `GetValueAsync(namespaceId, key, accountId)` | `GET …/namespaces/{namespace_id}/values/{key}` | raw body; per-key metadata from the `CF-KV-Metadata` response header |
| `GetStringAsync(namespaceId, key, accountId)` | `GET …/values/{key}` | `null` when the key is missing |
| `GetJsonAsync<T>(namespaceId, key, accountId)` | `GET …/values/{key}` | `default` when the key is missing |
| `PutValueAsync(namespaceId, key, KvPutValueRequest, accountId)` | `PUT …/values/{key}` | value is the HTTP body; `expiration` / `expiration_ttl` go in the query string |
| `PutStringAsync(namespaceId, key, value, expirationTtl, accountId)` | `PUT …/values/{key}` | |
| `PutJsonAsync<T>(namespaceId, key, value, expirationTtl, accountId)` | `PUT …/values/{key}` | `application/json` |
| `DeleteValueAsync(namespaceId, key, accountId)` | `DELETE …/values/{key}` | |
| `BulkWriteAsync(namespaceId, IEnumerable<KvBulkWriteOperation>, accountId)` | `PUT …/namespaces/{namespace_id}/bulk` | puts (text or base64) and deletes in one call |

Keys are percent-encoded with `CloudflareUrl.EncodeSegment`, and the `Uri` is built with path/query
canonicalization disabled so `%2F` survives — a key like `users/1/profile` stays one segment.
Key names are limited to 512 bytes by Cloudflare; TTLs under 60 seconds and expiration timestamps in
the past are rejected locally.

## R2

`IR2Service` — `client.R2`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListBucketsAsync(R2BucketListParameters?, accountId)` | `GET /accounts/{account_id}/r2/buckets` | `cursor`, `per_page` (1–1000), `name_contains`, `order` (only `name`), `direction`, `start_after` |
| `ListAllBucketsAsync(...)` | `GET …/r2/buckets` | walks the cursor |
| `GetBucketAsync(bucketName, R2Jurisdiction?, accountId)` | `GET …/r2/buckets/{bucket_name}` | `null` on 404 |
| `CreateBucketAsync(string name, R2LocationHint?, R2StorageClass?, R2Jurisdiction?, accountId)` | `POST …/r2/buckets` | body uses **camelCase**: `name`, `locationHint`, `storageClass` |
| `CreateBucketAsync(CreateR2BucketRequest, accountId)` | `POST …/r2/buckets` | |
| `UpdateBucketAsync(bucketName, R2StorageClass, R2Jurisdiction?, accountId)` | `PUT …/r2/buckets/{bucket_name}` | only `storageClass` is changeable |
| `DeleteBucketAsync(bucketName, R2Jurisdiction?, accountId)` | `DELETE …/r2/buckets/{bucket_name}` | |

Jurisdiction-restricted buckets are addressed with the **`cf-r2-jurisdiction`** request header
(`default`, `eu`, `us`, `fedramp`, `fedramp-high`) — never a query parameter — and `R2Jurisdiction`
is sent on every bucket call, not just create. `R2Jurisdiction.Unknown` sends no header.

The list response wraps its array: `result` is `{ "buckets": [...] }`. `CloudflarePage<T>.ToPage`
takes a selector for exactly this case, so callers still get a normal page.

Response fields are snake_case (`creation_date`, `storage_class`, `location`) while request fields
are camelCase (`locationHint`, `storageClass`); each property carries its own `[JsonPropertyName]`.

⛔ Object upload/download (those go through the S3-compatible endpoint or a signed URL, not the v4
JSON API), bucket policies, CORS, lifecycle rules, super slurp, and R2 data catalog.

---

## Cross-cutting behaviour

| Concern | Where |
| --- | --- |
| Envelope (`success`, `errors`, `messages`, `result`, `result_info`) | `CloudflareResponse<T>`, `CloudflareRawResponse` |
| Pagination | `CloudflarePage<T>`, `CloudflareResultInfo`, `CloudflarePagedParameters`, `CloudflareCursorParameters` |
| Query strings | `QueryStringBuilder` (`Uri.EscapeDataString` for values; `.`/`[`/`]` left readable in names) |
| URL construction | `CloudflareUrl`, `CloudflareRequest` |
| Bodies | `JsonRequestBody`, `TextRequestBody`, `BinaryRequestBody`, `MultipartRequestBody` |
| Serialization | `CloudflareJson.Default`, `CloudflareValueAttribute`, `CloudflareEnumConverter<T>` |
| Retry / backoff | `CloudflareTransport` — 408, 429, 5xx; `Retry-After` wins; exponential + jitter; clamped |
| Auth headers | `CloudflareTransport` — exactly one scheme per call |
| Diagnostics | `CloudflareResponse<T>.RayId` from `CF-RAY`; `CloudflareApiException.RayId` |
| Errors | `CloudflareApiException`, `CloudflareTransportException`, `CloudflareConfigurationException` |
| DI | `ServiceCollectionCloudflareExtensions` |

## Requesting more

Everything above follows one shape: a `CloudflareRequest`, a typed request body, a typed `result`,
and a method on the matching service. Adding an endpoint means adding those four pieces plus a test
against `StubHttpMessageHandler` — no infrastructure changes. Open an issue naming the endpoint and
the product area, and it is usually a small pull request.
