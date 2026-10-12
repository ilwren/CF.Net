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
| `GetAsync(string? zoneId)` | `GET /zones/{zone_id}` | returns `null` on 404 **and** on 403 — an unknown zone id is answered with 403/9109, and a zone outside the token's Zone Resources with 403/10000 |
| `GetByNameAsync(string name)` | `GET /zones?name=` | filters client-side for an exact match |
| `CreateAsync(string name, string type)` | `POST /zones` | `type` ∈ `full`, `partial`, `secondary`, `internal` |
| `CreateAsync(CreateZoneRequest)` | `POST /zones` | |
| `UpdateAsync(string? zoneId, UpdateZoneRequest)` | `PATCH /zones/{zone_id}` | `paused`, `plan`, `type`, `vanity_name_servers` |
| `SetPausedAsync(bool paused, string? zoneId)` | `PATCH /zones/{zone_id}` | convenience wrapper |
| `DeleteAsync(string zoneId)` | `DELETE /zones/{zone_id}` | |
| `RunActivationCheckAsync(string? zoneId)` | `POST /zones/{zone_id}/activation_check` | |
| `PurgeCacheAsync(PurgeCacheRequest, string? zoneId)` | `POST /zones/{zone_id}/purge_cache` | `purge_everything`, `files`, `tags`, `hosts`, `prefixes`, `ignore_tags` |

### Zone settings

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListSettingsAsync(string? zoneId)` | `GET /zones/{zone_id}/settings` | deprecated by Cloudflare, but the only way to see which settings a plan has and what shape each value takes |
| `GetSettingAsync(string settingId, string? zoneId)` | `GET /zones/{zone_id}/settings/{setting_id}` | `null` when the zone has no such setting |
| `UpdateSettingAsync(string settingId, UpdateZoneSettingRequest, string? zoneId)` | `PATCH /zones/{zone_id}/settings/{setting_id}` | body is `{"value":…}` **or** `{"enabled":bool}`; `EnsureValid()` rejects neither and both |
| `SetSettingValueAsync(string settingId, object? value, string? zoneId)` | `PATCH /zones/{zone_id}/settings/{setting_id}` | convenience: a string, number, object or array value |
| `SetSettingOnOffAsync(string settingId, bool on, string? zoneId)` | `PATCH /zones/{zone_id}/settings/{setting_id}` | sends `"on"`/`"off"`, which is how most switches are spelled |
| `UpdateSettingsAsync(IEnumerable<ZoneSettingChange>, string? zoneId)` | `PATCH /zones/{zone_id}/settings` | deprecated; the body is an **array** of `{id, value}`, not an object keyed by id |

`ZoneSetting` carries `Id`, `Value` (raw JSON, because Cloudflare's value type differs per setting),
`Editable`, `ModifiedOn`, plus `AsString()`/`AsNumber()`/`AsBoolean()`/`IsOn` for the common shapes.
`ZoneSettingIds` declares only the ids that appear in Cloudflare's published schema — every method takes
the id as a string, so any setting can be addressed. `ZoneSettingValues.On`/`.Off` are the switch literals.

⛔ Firewall rules, page rules, rate limits, WAF custom rules, Load balancing, Waiting Room, Turnstile —
separate phases. Certificate packs have their own section below; what is still missing there is ordering,
the validation restart, the quota and universal SSL settings.

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

---

## SSL certificate packs

`ISslService` — `client.Ssl`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(SslCertificatePackListParameters?, zoneId)` | `GET /zones/{zone_id}/ssl/certificate_packs` | `deploy` (`staging`/`production`), `status` (`all`, otherwise only the active packs are listed), `page`, `per_page` (5–50) |
| `ListAllAsync(...)` | `GET …/ssl/certificate_packs` | walks the pages, keeps `deploy`/`status`/`per_page` on every call, and stops on a short page when `result_info` is absent |
| `GetAsync(certificatePackId, zoneId)` | `GET …/ssl/certificate_packs/{certificate_pack_id}` | `null` on 404. A zone the token cannot see is answered with 403 (9109 unknown zone, 10000 out of scope) and raises, because that is a scoping problem, not a missing pack |
| `OrderAsync(OrderSslCertificatePackRequest, zoneId)` | `POST …/ssl/certificate_packs/order` | note the `/order` suffix - posting to the collection itself is not the endpoint. Body `{type:"advanced", hosts[], validation_method, validity_days, certificate_authority, cloudflare_branding?}`, every field checked locally first |
| `UpdateAsync(certificatePackId, UpdateSslCertificatePackRequest, zoneId)` | `PATCH …/ssl/certificate_packs/{certificate_pack_id}` | one endpoint, two unrelated operations: `cloudflare_branding` in the body changes branding, an **empty body** restarts validation |
| `SetCloudflareBrandingAsync(certificatePackId, enabled, zoneId)` | `PATCH …/certificate_packs/{certificate_pack_id}` | sends `{"cloudflare_branding":true|false}` - `false` is sent explicitly, because omitting the field would leave the setting alone |
| `RestartValidationAsync(certificatePackId, zoneId)` | `PATCH …/certificate_packs/{certificate_pack_id}` | sends `{}`; only a validation operation for a pack in `validation_timed_out`, and the challenge still has to be answered afterwards |
| `DeleteAsync(certificatePackId, zoneId)` | `DELETE …/ssl/certificate_packs/{certificate_pack_id}` | Advanced Certificate Manager packs only; returns the `id` Cloudflare echoes back |
| `GetQuotaAsync(zoneId)` | `GET …/ssl/certificate_packs/quota` | `{"advanced":{"allocated":…,"used":…}}`, plus `Remaining`; worth calling before ordering, since ordering past the quota fails |
| `GetUniversalSettingsAsync(zoneId)` | `GET /zones/{zone_id}/ssl/universal/settings` | one boolean, `enabled` |
| `SetUniversalSslAsync(enabled, zoneId)` | `PATCH …/ssl/universal/settings` | body `{"enabled":true|false}` - `false` is sent explicitly, because an empty PATCH would leave the setting alone |

Permissions are `SSL and Certificates Read` to list and read, `SSL and Certificates Write` to delete.

`SslCertificatePack` maps `id`, `zone_id`, `hosts`, `status`, `type`, `certificate_authority`, `priority`,
`signature`, `uploaded_on` and the `certificates` array (each with `id`, `hosts`, `status`, `bundle_method`
and `expires_on`). `status` has more values than the schema names in one place, so it stays a
string — `SslCertificatePackStatuses` holds the seven the documentation names (`initializing`,
`pending_validation`, `validation_timed_out`, `inactive`, `backup_issued`, `holding_deployment`, `deleted`)
and an unlisted one is never a deserialisation failure. `SslCertificatePackTypes` is the complete set of
eight (`universal`, `advanced`, `managed_hostname`, `mh_custom`, `sni_custom`, `total_tls`, `keyless`,
`legacy_custom`), `SslCertificateAuthorities` the three (`google`, `lets_encrypt`, `ssl_com`). Issuance
fields the model does not name (`validation_method`, `validity_days`, the primary certificate, …) arrive in
`AdditionalData` rather than being dropped.

Ordering is a paid Advanced Certificate Manager feature, and it is not finished when the call returns: the
hosts must contain the zone apex, at most 50 of them, and validation still has to be answered - a TXT record
or an HTTP token for `txt`/`http`, a clicked link for `email` - so the pack sits in `pending_validation`
until then. `validity_days` accepts 14, 30, 90 and 365 (730, 1095 and 5475 belong to the separate
`request_validity` model and are rejected locally), the authorities are `google`, `lets_encrypt` and
`ssl_com`, and `type` is always `advanced`, which is the only value the endpoint defines. Anything this
release does not model can still be sent through the request's `AdditionalData`.

`SslCertificatePackStatuses` also names `pending_issuance` and `pending_deployment`, the two steps between an
answered challenge and a certificate being served.

**Turning Universal SSL off is not a cosmetic change.** Cloudflare removes the zone's active universal
certificates from the edge and stops ordering new ones, so unless an advanced certificate pack or an uploaded
custom certificate covers the hostname, HTTPS simply stops working - and HSTS, Always Use HTTPS,
Opportunistic Encryption, Onion Routing and any page rule redirecting to HTTPS turn that into a hard failure
instead of a fallback to HTTP. `UniversalSslSettings` keeps `Enabled` nullable and reports `unknown` when
Cloudflare omits it, so "off" and "not answered" are never the same thing.

A pack also carries what it takes to finish validating it: `validation_method`, `validation_records` (a
`txt_name`/`txt_value` pair for a TXT challenge, a `cname`/`cname_target` pair for DCV delegation, `emails`
for an e-mail challenge, plus that record's `status`) and `validation_errors`, alongside `created_on`,
`validity_days` and `primary_certificate`. The TXT pair is exactly what `client.DnsRecords.CreateTxtRecordAsync`
takes, so answering a challenge is two calls in this library and no copy-pasting from the dashboard.

⛔ DCV delegation and validation method (`/zones/{zone_id}/ssl/verification`), auto-origin TLS KEX, automatic
SSL/TLS enrolment and the SSL/TLS recommendation.

## Users and accounts

`IUserService` — `client.User`; `IAccountsService` — `client.Accounts`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `User.VerifyTokenAsync()` | `GET /user/tokens/verify` | for a **user-owned** token (`cfut_...`); answers `1000 Invalid API Token` for an account-owned one |
| `User.VerifyAccountTokenAsync(string? accountId)` | `GET /accounts/{account_id}/tokens/verify` | for an **account-owned** token (`cfat_...`), the service principal CI should use |
| `User.GetAsync()` | `GET /user` | user-scoped, so an account-owned token cannot read it |
| `User.CheckConnectionAsync()` *(also on `ICloudflareClient`)* | `GET /user/tokens/verify`, `GET /accounts/{account_id}/tokens/verify`, or `GET /user` | never throws; picks the verify endpoint from the token prefix and falls back to the account one when the user-scoped call is rejected; reports scheme, status, `ErrorCode`, `IsTimeout`, elapsed |
| `Accounts.ListAsync(AccountListParameters?)` | `GET /accounts` | `page`, `per_page`, `name` |
| `Accounts.GetAsync(string? accountId)` | `GET /accounts/{account_id}` | returns `null` on 403/404 |

`CloudflareClientOptions.HasAccountOwnedToken` says whether the configured token is a `cfat_` one, and
`CloudflareDefaults.TokenPrefixes` names the three prefixes Cloudflare issues (`cfut_`, `cfat_`, `cfk_`).
Credentials created before 2026 carry no prefix, so a prefix check can only ever be a hint - which is why
`CheckConnectionAsync` retries against the account endpoint rather than trusting the prefix alone.

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
| `ListDomainsAsync(accountId)` | `GET /accounts/{account_id}/workers/domains` | custom domains; no query parameters are sent, because the published schema documents none and Cloudflare rejects ones it does not know |
| `GetDomainAsync(domainId, accountId)` | `GET …/workers/domains/{domain_id}` | `null` on 404 |
| `AttachDomainAsync(AttachWorkerDomainRequest, accountId)` | `PUT …/workers/domains` | a PUT to the collection is how Cloudflare spells "attach"; `EnsureValid()` refuses a domain that routes nothing (hostname routing off **and** preview routing off) |
| `AttachDomainAsync(hostname, workerName, zoneId, accountId)` | `PUT …/workers/domains` | convenience; `zoneId` falls back to the configured zone |
| `DeleteDomainAsync(domainId, accountId)` | `DELETE …/workers/domains/{domain_id}` | detaches |
| `ListDomainsForWorkerAsync(workerName, accountId)` | `GET /accounts/{account_id}/workers/domains` | convenience filter, not an endpoint: lists the account collection once and keeps the attachments whose `service` is that Worker (case-insensitive) |

Binding types modelled: `kv_namespace`, `r2_bucket`, `plain_text`, `secret_text`, `service`,
`vpc_service`, `do_namespace`, `d1_database_binding`, `queue`, `analytics_engine`, `assets`,
`browser`, `vectorize`, `hyperdrive`, `ai`. Module content types: `application/javascript+module`,
`text/x-python`, `application/wasm`, `buffer`, `base64`, `text`.

`WorkerDomain` carries `id`, `cert_id`, `enabled`, `previews_enabled`, `hostname`, `service`, `zone_id`,
`zone_name` and the deprecated `environment`.

Custom domains are an **account-level** collection only — the published schema has no
`…/workers/scripts/{script_name}/domains` endpoint and documents no query parameters for the list either, so
"which domains does this Worker serve" is answered by filtering locally (`ListDomainsForWorkerAsync`) rather
than by a server-side query.

⛔ The newer JSON-only flow (`POST /workers/workers` → `…/versions` → `…/deployments`), static asset
upload sessions, Workers for Platforms dispatch namespaces, cron triggers, query filters on the domain
list, tails, observability configuration, and Workers AI.

## Pages

`IPagesProjectsService` — `client.Pages`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(PagesProjectListParameters?, accountId)` | `GET /accounts/{account_id}/pages/projects` | sends `page`, `per_page` (always paired — Pages answers a bare page size with `8000024`) and `search`; retries once with no query at all when Cloudflare answers `8000024 Invalid list options provided`, which it does for some accounts even though the schema documents both paging parameters. `search` is not in the published schema and is only sent when set |
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
| `RollbackDeploymentAsync(projectName, deploymentId, accountId)` | `POST …/deployments/{deployment_id}/rollback` | rolls **production** back to the deployment in the path; no body, returns the deployment now serving |
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
| `QueryRawAsync(databaseId, sql, parameters, accountId)` | `POST …/d1/database/{database_id}/raw` | same body and permissions as `/query`; Cloudflare's performance-optimised variant returns each row as an **array** of column values — read them with `RowArrays()` |
| `BatchRawAsync(databaseId, IEnumerable<D1Query>, accountId)` | `POST …/raw` | one `{batch:[{sql,params}, …]}` body |
| `ExecuteRawAsync(databaseId, D1QueryRequest, accountId)` | `POST …/raw` | send a prebuilt request verbatim |
| `ListBackupsAsync(databaseId, D1BackupListParameters?, accountId)` | `GET …/d1/database/{database_id}/backup` | |
| `GetBackupAsync(databaseId, backupId, accountId)` | `GET …/backup/{backup_id}` | `null` on 404 |
| `RestoreBackupAsync(databaseId, backupId, accountId)` | `POST …/backup/{backup_id}/restore` | destructive |
| `SetReadReplicationAsync(databaseId, mode, accountId)` | `PATCH …/d1/database/{database_id}` | `mode` ∈ `auto`, `disabled`; turning it off can take hours to remove every replica |
| `UpdateAsync(databaseId, UpdateD1DatabaseRequest, accountId)` | `PUT …/d1/database/{database_id}` | the PUT takes the same body as the PATCH — `read_replication` and nothing else, so **renaming a database is not available** |
| `UndeleteAsync(databaseId, accountId)` | `POST …/d1/database/{database_id}/undelete` | works only while D1 still holds the deleted database |
| `ExportAsync(databaseId, D1ExportRequest?, accountId)` | `POST …/d1/database/{database_id}/export` | body `{output_format:"polling", current_bookmark?, dump_options?{no_data?, no_schema?, tables?[]}}`; `output_format` is required and `polling` is the only value defined |
| `ExportAndWaitAsync(databaseId, D1DumpOptions?, pollInterval, accountId)` | `POST …/export` | starts the export and keeps polling with the returned `at_bookmark` until the status is `complete` or `error` |
| `ImportAsync(databaseId, D1ImportRequest, accountId)` | `POST …/d1/database/{database_id}/import` | one endpoint, three actions: `init`, `ingest`, `poll` |
| `InitialiseImportAsync(databaseId, etag, accountId)` | `POST …/import` | `{"action":"init","etag":…}` → `filename` + the presigned `upload_url` |
| `IngestImportAsync(databaseId, etag, filename, accountId)` | `POST …/import` | `{"action":"ingest","etag":…,"filename":…}`; imports block the database while they run |
| `PollImportAsync(databaseId, currentBookmark, accountId)` | `POST …/import` | `{"action":"poll","current_bookmark":…}`, one poll |
| `WaitForImportAsync(databaseId, currentBookmark, pollInterval, accountId)` | `POST …/import` | polls until `complete` or `error` |

`D1QueryResult` exposes `Results` (raw `JsonElement` rows), `Success`, `Meta` and the ergonomics
`Rows<T>()`, `FirstColumn<T>(name)` and `RowCount`. `D1QueryMeta` carries `changes`, `duration`,
`last_row_id`, `rows_read`, `rows_written`, `size_after`, `changed_db`, `served_by_colo`,
`served_by_primary`, `served_by_region` and `timings.sql_duration_ms`, plus `RowArrays()` for the raw
endpoint.

`RowArrays()` and the object-shaped helpers are mutually exclusive: `/query` returns every row as a JSON
object, so `Rows<T>()` and `FirstColumn<T>(name)` work and `RowArrays()` is empty; `/raw` returns every row
as a JSON array in the column order of the statement you sent, so `RowArrays()` works and the named helpers
find nothing.

**Export and import are polled operations, and the polling is part of the contract.** Cloudflare cancels an
export that stops being polled, and the database cannot serve queries while a large one runs, so
`ExportAndWaitAsync` and `WaitForImportAsync` do the waiting (two seconds between polls by default, never
negative, cancellable) while `ExportAsync` / `ImportAsync` leave each step in your hands. Both stop early and
hand back what Cloudflare said if a response carries no `at_bookmark` to continue with, since polling again
without one would start a second operation instead of following the first.

A finished export reports `status: "complete"` with `result: { filename, signed_url }`; a failed one reports
`status: "error"` with an `error` message. Both arrive as HTTP 200, so neither throws - check
`IsComplete` / `IsFailed` on `D1ExportResult` and `D1ImportResult`. Downloading the dump from `signed_url`
is an ordinary HTTP GET that needs no Cloudflare authentication.

An import is three calls around one upload: `init` (with the MD5 etag of the file, which
`D1ImportRequest.ComputeEtag` produces) returns a presigned R2 `upload_url` and a `filename`; you PUT the SQL
to that URL yourself; `ingest` starts the import; `poll` follows it to `result: { final_bookmark, num_queries,
meta }`. The upload is deliberately **not** in this library - it is not a Cloudflare API call, it needs no
token, and sending one alongside a presigned signature risks the request being rejected:

```csharp
var sql = await File.ReadAllTextAsync("dump.sql");
var etag = D1ImportRequest.ComputeEtag(sql);          // MD5, which is what Cloudflare asks for

var init = await client.D1.InitialiseImportAsync(databaseId, etag);
await using var uploader = new HttpClient();           // no Cloudflare token on this one
await uploader.PutAsync(init.UploadUrl!, new StringContent(sql));

var ingest = await client.D1.IngestImportAsync(databaseId, etag, init.Filename!);
var done = await client.D1.WaitForImportAsync(databaseId, ingest.AtBookmark!);
if (done.IsFailed) throw new InvalidOperationException(done.Error);
```

Still unwrapped: D1 time travel as a query option, and the `meta` fields of an import beyond `served_by` and
`duration` are kept in `AdditionalData` rather than guessed at.

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
| `UpdateBucketAsync(bucketName, R2StorageClass, R2Jurisdiction?, accountId)` | `PATCH …/r2/buckets/{bucket_name}` | no body: the storage class travels in the **`cf-r2-storage-class`** header. Only the default storage class is changeable, and it applies to objects uploaded afterwards |
| `DeleteBucketAsync(bucketName, R2Jurisdiction?, accountId)` | `DELETE …/r2/buckets/{bucket_name}` | |

Jurisdiction-restricted buckets are addressed with the **`cf-r2-jurisdiction`** request header
(`default`, `eu`, `us`, `fedramp`, `fedramp-high`) — never a query parameter — and `R2Jurisdiction`
is sent on every bucket call, not just create. `R2Jurisdiction.Unknown` sends no header.

The list response wraps its array: `result` is `{ "buckets": [...] }`. `CloudflarePage<T>.ToPage`
takes a selector for exactly this case, so callers still get a normal page.

Response fields are snake_case (`creation_date`, `storage_class`, `location`) while request fields
are camelCase (`locationHint`, `storageClass`); each property carries its own `[JsonPropertyName]`.

**Bucket CORS is not part of the v4 API.** The published R2 schema has exactly five methods — list, get,
create, patch and delete — and no CORS resource. Cloudflare configures CORS through the S3-compatible
endpoint instead (`PutBucketCors` / `GetBucketCors`), which speaks AWS Signature Version 4 with the R2 access
key id and secret from the dashboard rather than a Cloudflare API token. `CloudflareDefaults.R2.S3Endpoint(accountId)`
builds that URL — `https://{account_id}.r2.cloudflarestorage.com` — so an S3 SDK or the AWS CLI can be pointed
at it; this library does not sign S3 requests.

⛔ Object upload/download (those go through the S3-compatible endpoint or a signed URL, not the v4
JSON API), bucket CORS (S3 API only, see above), bucket policies, lifecycle rules, super slurp, and R2 data
catalog.

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
