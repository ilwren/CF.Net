# API coverage

Every Cloudflare API v4 endpoint this release wraps, with the CF.Net method that calls it.
Base URL: `https://api.cloudflare.com/client/v4`.

`{account_id}` and `{zone_id}` fall back to `CloudflareClientOptions.AccountId` /
`.ZoneId` when the corresponding argument is left `null`, and raise a
`CloudflareConfigurationException` when neither is available.

Status legend: ✅ wrapped · ⛔ deliberately out of scope for this phase.

**Paid features are out of scope.** An endpoint that only answers for a paid plan or a paid add-on - SSL for
SaaS beyond what a free account is entitled to, WAF custom rules and rate limiting where the account is not
entitled to the ruleset, waiting rooms, load balancing - is not part of the coverage target, and "every
endpoint" does not mean those. What is already wrapped stays wrapped: the live suite skips such a call as an
entitlement refusal (`1404`, `50002`, a `403` with no permission behind it) rather than failing it, and the
skip reason names the plan or permission the account would need.

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

Two fields in the published `Zone` schema are objects rather than the identifiers they look like:
`tenant` is `{ id, name }` and `tenant_unit` is `{ id }`, so both are modelled as `CloudflareIdentifier`.
Getting that wrong is not a small mistake — one field of one zone that does not fit its model discards the
whole envelope, and because the transport used to fall back to "success, no result" for a 2xx it could not
parse, a zone that answered 200 with 22 fields was reported as unreadable and every zone-scoped test
skipped or failed on an assertion that named nothing. The transport now throws on a 2xx it cannot read,
quoting the failing JSON path (`$.result[0].tenant` is what caught this), and
`ZonePublishedExampleTests` feeds Cloudflare's own 200 example through the models so the next drift shows
up offline instead of in a live run.

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
the id as a string, so any setting can be addressed. One caveat a live run surfaced: a setting is not
always answered with the id it was requested by. `GET /zones/{zone_id}/settings/origin_tls_compliance_modes`
reports `"id": "origin_tls_cmpl"` — an abbreviation, not a truncation: the first fifteen characters of the
request id would be `origin_tls_comp`, so Cloudflare is compressing `compliance` to `cmpl` — while
`auto_origin_tls_kex` and `ssl_automatic_mode`, both longer, come back intact. `ZoneSettingIds` carries both spellings
(`OriginTlsComplianceModes` for the path, `OriginTlsComplianceModesReportedId` for the response) and the
integration test accepts either, so Cloudflare fixing it will not turn the run red. The library never
rewrites the id: what arrives is what you get. `ZoneSettingValues.On`/`.Off` are the switch literals.

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
| `BatchAsync(DnsRecordBatch, includeShadowMetadata, zoneId)` | `POST /zones/{zone_id}/dns_records/batch` | deletes, patches, puts and posts in one call, executed in that order in a single transaction; propagation is not atomic. `EnsureValid()` rejects an empty batch (Cloudflare answers it with `9207`) and any delete or patch with no id. Cloudflare does not publish the result, so `DnsRecordBatchResult` was written from a live answer: the four groups come back as they went in, an unused group as an explicit `null`, a used one as **complete records** — a delete echoes what it removed, name and content and timestamps included |
| `ListScannedRecordsAsync(perPage, zoneId)` | `GET /zones/{zone_id}/dns_records/scan/review` | a zone that has never been scanned answers 200 with an empty list, verified live |

Record types modelled with constants and typed data: `A`, `AAAA`, `CAA`, `CERT`, `CNAME`, `DNSKEY`,
`DS`, `HTTPS`, `LOC`, `MX`, `NAPTR`, `NS`, `PTR`, `SMIMEA`, `SRV`, `SSHFP`, `SVCB`, `TLSA`, `TXT`,
`URI`. Structured payloads are `DnsRecordSrvData`, `DnsRecordLocData`, `DnsRecordCaaData`,
`DnsRecordSshfpData`, `DnsRecordDnskeyData`, `DnsRecordTlsaData`, `DnsRecordUriData`, … reachable
through `DnsRecord.Data` and `DnsRecord.GetData<T>()`.

`BatchAsync` and `ListScannedRecordsAsync` above were verified against the live API before being modelled:
the reach matrix probes `POST …/dns_records/batch` with an empty body, which Cloudflare refuses with `9207`
without touching a record, and `GET …/dns_records/scan/review`, which answers 200 with an empty list for a
zone that has never been scanned. Still unwrapped, both under the same **DNS Read / DNS Write** permissions:

| Endpoint | What it does | Why it is not wrapped yet |
| --- | --- | --- |
| `POST /zones/{zone_id}/dns_records/scan/trigger` | starts a scan for records at the registrar, for a zone being onboarded | neither the request body nor the result is published, and a scan is a real action against the zone, so it is not something to probe with an empty body |
| `POST /zones/{zone_id}/dns_records/scan/review` | accepts or rejects the scanned records | same, and worse: it writes records into the zone |

`POST /zones/{zone_id}/dns_records/scan` is deliberately left out: Cloudflare marks it **Deprecated** and
`scan/trigger` is the supported replacement, the same call made about `custom_ns`. There is no
`GET /zones/{zone_id}/dns_records/usage` in the published API — an earlier note here claimed there was, and
the records index does not list it; that note was wrong rather than the endpoint being missed.

⛔ DNS analytics and secondary zone transfers.

⛔ `GET` and `PUT /zones/{zone_id}/custom_ns`. Both are **deprecated** by Cloudflare in favour of the DNS
settings view, both serve the partner flow (`/tenants/{tenant_tag}/custom_ns`), and the supported
replacement is already wrapped here through the zone settings above. That is why these are skipped where the
deprecated `origin_tls_client_auth` endpoints were not: those are still the only way to read and dismantle an
existing zone-level client certificate, so a wrapper is the difference between supported and stranded, while
these have a live replacement. The `PUT` is also worth knowing about rather than calling — its `result` is a
bare string, not an object, and its response reports which of the 1–5 nameserver sets the zone is on.

---

## DNSSEC

`IDnssecService` — `client.Dnssec`. Cloudflare files DNSSEC under `dns`, beside `dns_records`, and it takes
`DNS Read` / `DNS Write` rather than the zone-settings permissions, so it has its own service instead of
hanging off `IZonesService`.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `GetAsync(zoneId)` | `GET /zones/{zone_id}/dnssec` | `Dnssec`, all fourteen published fields: `status`, `modified_on`, the DS record and its parts (`ds`, `key_tag`, `algorithm`, `digest`, `digest_algorithm`, `digest_type`, `flags`, `key_type`, `public_key`) and the three signing switches |
| `UpdateAsync(UpdateDnssecRequest, zoneId)` | `PATCH /zones/{zone_id}/dnssec` | a **merge**, not a replace: only the fields that are set are sent, so the signing switches can change without touching the enabled state |
| `EnableAsync(zoneId)` / `DisableAsync(zoneId)` | `PATCH …/dnssec` | conveniences for `{"status":"active"}` and `{"status":"disabled"}` |
| `DeleteAsync(zoneId)` | `DELETE /zones/{zone_id}/dnssec` | the `result` is a **bare string**, not an object, so this returns `Task<string?>` |
| `ListZoneSigningKeysAsync(zoneId)` | `GET /zones/{zone_id}/dnssec/zsk` | an array, with no paging and no filters |

Two status lists, deliberately not one:

- `DnssecStatuses.All` — the five the response reports: `active`, `pending`, `disabled`, `pending-disabled`,
  `error`.
- `DnssecSettableStatuses.All` — the two the PATCH accepts: `active`, `disabled`.

`UpdateDnssecRequest.EnsureValid()` rejects the other three locally, because `pending`, `pending-disabled`
and `error` are outcomes Cloudflare computes from the desired state plus what the registrar has published,
not states a caller can ask for. `EnsureValid()` also rejects a request that sets nothing at all: an empty
PATCH is a round trip that cannot do anything.

The waiting states are the part that trips people up. Enabling returns `pending` and stays there until the DS
record from `Dnssec.Ds` is published at the registrar; disabling returns `pending-disabled` until the
registrar drops it. Neither is a failure, and neither can be finished through this API — poll `GetAsync`.
`Dnssec.IsActive` is true only for `active`, and `IsPending` covers both waiting states. Disabling before the
DS record is removed breaks resolution for validating resolvers, so the record goes first and
`DisableAsync` follows.

The ZSK response is spelled in **PascalCase** — `DNSKEY`, `Hdr`, `Rdlength`, `Rrtype`, `Ttl` — because
Cloudflare is serialising DNS library structs straight out, which no other endpoint in the API does; the names
are pinned with explicit `[JsonPropertyName]` rather than left to the snake_case policy.
`DnssecRecordHeader` carries all five published header fields, and `DnssecDnsKey.IsKeySigningKey` reads the
`257`-vs-`256` flag distinction. The schema counts five fields on the ZSK wrapper and five on `DNSKEY`, but
only `DNSKEY`'s contents are published: `DnssecZoneSigningKey` types that one and leaves `Location`, `Name`
and the other two in `AdditionalData` rather than inventing types for fields whose types have not been
confirmed — a guessed property is worse than an untyped one, because it looks like a contract.

⛔ Nothing in DNSSEC itself. The undocumented remainder of the ZSK payload is described above.

---

## SSL

`ISslService` — `client.Ssl`. Certificate packs, Universal SSL, validation details, the pack quota,
and the two SSL enrolment settings Cloudflare serves from the zone settings path.

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
| `GetVerificationAsync(retry, zoneId)` | `GET …/ssl/verification` | an **array**, one entry per certificate being validated: `cert_pack_uuid`, `certificate_status`, `validation_method`, `verification_status`, `verification_type`, `brand_check`, `signature`, and `verification_info` (`record_name`, `record_target`, `policy_email`) - i.e. precisely which record the authority is still waiting for. `retry: true` adds `retry=true`, making Cloudflare re-run the check now instead of at its next scheduled attempt |
| `UpdateValidationMethodAsync(certificatePackId, validationMethod, zoneId)` | `PATCH …/ssl/verification/{certificate_pack_id}` | body `{"validation_method":…}` - switches the method and validates with it immediately; returns `{status, validation_method}` |
| `RevalidateAsync(certificatePackId, zoneId)` | `PATCH …/ssl/verification/{certificate_pack_id}` | **empty body `{}`** = "request an immediate validation check" using the method already in use |
| `GetQuotaAsync(zoneId)` | `GET …/ssl/certificate_packs/quota` | `{"advanced":{"allocated":…,"used":…}}`, plus `Remaining`; worth calling before ordering, since ordering past the quota fails |
| `GetUniversalSettingsAsync(zoneId)` | `GET /zones/{zone_id}/ssl/universal/settings` | one boolean, `enabled` |
| `SetUniversalSslAsync(enabled, zoneId)` | `PATCH …/ssl/universal/settings` | body `{"enabled":true|false}` - `false` is sent explicitly, because an empty PATCH would leave the setting alone |
| `GetAutoOriginTlsKexAsync(zoneId)` | `GET /zones/{zone_id}/settings/auto_origin_tls_kex` | `{id, enabled, modified_on}` - note `enabled`, not the `value` almost every other zone setting uses, which is why it gets its own model instead of reusing `ZoneSetting` |
| `SetAutoOriginTlsKexAsync(enabled, zoneId)` | `PATCH …/settings/auto_origin_tls_kex` | body `{"enabled":true|false}`; the flag is required by the schema and always sent explicitly |
| `GetAutomaticSslAsync(zoneId)` | `GET …/settings/ssl_automatic_mode` | `{id, editable, modified_on, value, next_scheduled_scan?}`; `value` is `auto` (enrolled) or `custom` (not) |
| `SetAutomaticSslAsync(mode, zoneId)` | `PATCH …/settings/ssl_automatic_mode` | body `{"value":"auto"\|"custom"}`; any other value is refused locally, because Cloudflare answers it with error 1008, "Invalid SSL/TLS encryption configuration value, only auto or custom accepted" |
| `EnableAutomaticSslAsync(zoneId)` / `DisableAutomaticSslAsync(zoneId)` | `PATCH …/settings/ssl_automatic_mode` | the same call with `auto` / `custom` |
| `GetOriginTlsComplianceModesAsync(zoneId)` | `GET /zones/{zone_id}/settings/origin_tls_compliance_modes` | `{id, editable, value[], modified_on}`; `null` on 404. `value` is an **array**, not the scalar `value` most zone settings use, so the generic `ZoneSetting` model would leave it in `AdditionalData` |
| `ReplaceOriginTlsComplianceModesAsync(UpdateOriginTlsComplianceModesRequest, zoneId)` | `PUT …/settings/origin_tls_compliance_modes` | body `{"value":["fips","pqh"]}` - the complete new list. An empty list clears the constraint; a missing one is refused locally |
| `ChangeOriginTlsComplianceModesAsync(UpdateOriginTlsComplianceModesRequest, zoneId)` | `PATCH …/settings/origin_tls_compliance_modes` | **also a full replace, not a merge** - same body, same effect, different verb |
| `ClearOriginTlsComplianceModesAsync(zoneId)` | `DELETE …/settings/origin_tls_compliance_modes` | the delete response drops `value` and returns only `{id, editable, modified_on}` |
| `AnalyzeCertificateAsync(AnalyzeSslCertificateRequest, zoneId)` | `POST …/ssl/analyze` | body `{certificate?, bundle_method?}` with `bundle_method` one of `ubiquitous`/`optimal`/`force`. Returns **raw `JsonElement?`** on purpose - see below |
| `GetRecommendationAsync(zoneId)` | `GET …/ssl/recommendation` | **deprecated**, and the path is **singular** - `recommendation`, not `recommendations`. It reports the automatic-mode setting rather than a suggested encryption mode, so prefer `GetAutomaticSslAsync` |

Permissions are `SSL and Certificates Read` to list and read, `SSL and Certificates Write` to delete. The
verification endpoints accept any one of four: `SSL and Certificates` or `Access: Mutual TLS Certificates`,
read or write.

`SslCertificatePack` maps `id`, `zone_id`, `hosts`, `status`, `type`, `certificate_authority`, `priority`,
`signature`, `uploaded_on` and the `certificates` array (each with `id`, `hosts`, `status`, `bundle_method`,
`expires_on`, `issuer`, `signature`, `priority`, `uploaded_on`, `modified_on`, `zone_id` and
`geo_restrictions.label`, the last being `us`, `eu` or `highest_security`). `status` has more values than the schema names in one place, so it stays a
string — `SslCertificatePackStatuses` holds the seven the documentation names (`initializing`,
`pending_validation`, `validation_timed_out`, `inactive`, `backup_issued`, `holding_deployment`, `deleted`)
and an unlisted one is never a deserialisation failure. `SslCertificatePackTypes` is the complete set of
eight (`universal`, `advanced`, `managed_hostname`, `mh_custom`, `sni_custom`, `total_tls`, `keyless`,
`legacy_custom`), `SslCertificateAuthorities` the three (`google`, `lets_encrypt`, `ssl_com`). The issuance
fields are modelled too (`validation_method`, `validity_days`, `primary_certificate`, `validation_records`,
`validation_errors`, `created_on`); what is not - a restriction label on an individual certificate, anything
Cloudflare adds later - arrives in `AdditionalData` rather than being dropped.

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

**There are two different "try again" calls, and they are not interchangeable.** `RestartValidationAsync`
PATCHes the *certificate pack* with an empty body and restarts a pack that reached `validation_timed_out`;
`RevalidateAsync` PATCHes `ssl/verification/{certificate_pack_id}` with an empty body and asks Cloudflare to
re-run the DCV check now, while `GetVerificationAsync(retry: true)` does the same for every certificate in the
zone in one call. `UpdateValidationMethodAsync` is the fourth: it changes the method (`txt`, `http`, `email`
or `cname` - `cname`, meaning DCV delegation, is accepted here but *not* when ordering a pack, so
`SslValidationMethods.Cname` is deliberately absent from `SslValidationMethods.All`) and validates with it
straight away.

**Two of these are zone settings that Cloudflare files under SSL.** Auto-Origin TLS KEX and Automatic SSL/TLS
enrolment are served from `/zones/{zone_id}/settings/…`, so `client.Zones.GetSettingAsync("auto_origin_tls_kex")`
reaches them too - but the generic `ZoneSetting` model would leave their payload in `AdditionalData`, because one
reports `enabled` instead of `value` and the other adds `next_scheduled_scan`. `client.Ssl` wraps both typed, and
`ZoneSettingIds` now names the two ids.

Auto-Origin TLS KEX has Cloudflare scan the zone's origins periodically, work out which key-exchange algorithm
they prefer, and send it first in the TLS ClientHello; it may only choose from what the zone's
`origin_tls_compliance_modes` setting allows, and turning it off returns to the default ordering. Automatic
SSL/TLS `auto` enrols the zone so Cloudflare manages its encryption mode, `custom` leaves that to the zone -
and opting out does not itself change the mode the zone is on.

**`ssl/analyze` returns raw JSON, and that is not laziness.** Cloudflare's own schema types the result as
`unknown` - the official Go SDK declares `AnalyzeNewResponse = interface{}` - and only the prose describes it
("the set of hostnames, the signature algorithm, and the expiration date"). Inventing property names for that
would produce a model that looks like a contract and is really a guess, so `AnalyzeCertificateAsync` types the
*request* (`certificate`, `bundle_method`) and hands back the `JsonElement` for the answer. The same reasoning
put `geo_restrictions` in `AdditionalData` until the field names were confirmed, and they now are, so it is
typed.

**`origin_tls_compliance_modes` is the fence Auto-Origin TLS KEX runs inside.** Each mode narrows the key
exchanges the scan is allowed to prefer: `fips` keeps it to FIPS-approved curves, `pqh` asks for a
post-quantum hybrid one. Several modes combine as the **intersection** of the algorithms each permits, and a
combination whose intersection is empty is rejected rather than quietly loosened - which is a 4xx this library
cannot predict without Cloudflare's current algorithm tables, so it does not try.

**Both write verbs replace the whole list.** Cloudflare documents `PATCH` here as a full replace too, not a
merge, so removing one mode means sending the others; `ReplaceOriginTlsComplianceModesAsync` (PUT) and
`ChangeOriginTlsComplianceModesAsync` (PATCH) therefore send identical bytes and differ only in the verb, and
a unit test asserts exactly that. `UpdateOriginTlsComplianceModesRequest.Clear()` sends `{"value":[]}`, which
is how the constraint is cleared without deleting the setting.

**Modes are strings, not an enum.** The schema says future modes may appear - it names `cnsa2` as an example -
and that clients should treat unknown values as opaque strings, so `Value` is a `List<string>` and
`OriginTlsComplianceModeValues.All` lists only the two documented today. Nothing rejects a mode this release
has not heard of, in either direction. (Cloudflare's own description of the response's `id` calls it "the
identifier of the caching setting", which is a copy-paste mistake in the published schema: nothing here
caches.)

The origin-side mutual TLS resource, `origin_tls_client_auth`, is a sibling of `ssl` rather than part of it
and has its own section below, as do the SSL for SaaS custom hostnames.

## SSL for SaaS (custom hostnames)

`ICustomHostnamesService` — `client.CustomHostnames`. Hostnames on other people's domains that CNAME into a
zone and get their certificates from Cloudflare: `/zones/{zone_id}/custom_hostnames`.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(CustomHostnameListParameters?, zoneId)` | `GET /zones/{zone_id}/custom_hostnames` | filters: `id`, `certificate_authority`, `custom_origin_server`, `direction` (`asc`/`desc`), `hostname.contain` / `hostname.exact` / `hostname.startsWith`, `hostname_status` (16 values), `order` (`ssl`/`ssl_status`), `ssl` (**`1`/`0`**, not `true`/`false`), `ssl_status` (21 values), `wildcard`, `page`, `per_page` (5–1000). `id` and the three hostname filters are mutually exclusive, and Cloudflare rejects a request that combines them |
| `ListAllAsync(...)` | `GET …/custom_hostnames` | walks the pages and copies all twelve filters onto every call, so a filter cannot be dropped halfway through |
| `GetAsync(customHostnameId, zoneId)` | `GET …/custom_hostnames/{custom_hostname_id}` | `null` on 404 |
| `CreateAsync(CreateCustomHostnameRequest, zoneId)` | `POST …/custom_hostnames` | body `{hostname, custom_metadata?, custom_origin_server?, custom_origin_sni?, ssl?}`, checked locally first: the hostname is required and `custom_cert_bundle` holds one or two complete pairs |
| `CreateAsync(hostname, dcvMethod = "http", wildcard = false, zoneId)` | `POST …/custom_hostnames` | the common case - a Cloudflare-issued certificate, sent as `{"method":…,"type":"dv","wildcard":…}` |
| `UpdateAsync(customHostnameId, UpdateCustomHostnameRequest, zoneId)` | `PATCH …/custom_hostnames/{custom_hostname_id}` | `custom_metadata`, `custom_origin_server`, `custom_origin_sni`, `ssl`. **There is no `hostname` field here** - a custom hostname cannot be renamed, only deleted and created again |
| `RetryValidationAsync(customHostnameId, dcvMethod = "http", zoneId)` | `PATCH …/custom_hostnames/{custom_hostname_id}` | the same endpoint with the body already built: `{"ssl":{"method":…,"type":"dv","wildcard":false}}` |
| `DeleteAsync(customHostnameId, zoneId)` | `DELETE …/custom_hostnames/{custom_hostname_id}` | removes the hostname **and any certificate issued for it**, so HTTPS for it stops |

**Two calls, one lifecycle.** Creating a hostname does not produce a working certificate; it produces a
challenge. The response carries `ownership_verification` (a TXT record) and `ownership_verification_http`
(a token to serve over HTTP), plus `ssl.validation_records` naming exactly what the authority is waiting for
and `ssl.validation_errors` for what already went wrong. `status` tracks the hostname and `ssl.status` tracks
the certificate, and they move independently - `ssl.status` is the one to poll, walking
`initializing → pending_validation → pending_issuance → pending_deployment → active`, with the `*_timed_out`
values where it stops if nobody answers the challenge.

**PATCH does double duty.** Sending SSL configuration that matches what is already there tells Cloudflare to
run domain control validation again. That is how an `http` hostname finishes validating once its CNAME
finally resolves, and how a hostname moves from one validation method to another - `RetryValidationAsync` is
that call with the body built for you.

**Three asymmetries worth knowing.** `digicert` can issue a custom hostname's certificate but is not accepted
as a `certificate_authority` *filter*, and is not in the advanced-certificate-pack authority set either, so
the three sets differ and are modelled separately: `CustomHostnameCertificateAuthorities.All`,
`.Filterable`, and `SslCertificateAuthorities.All`. In the same query string, the `ssl` filter wants `1` and
`0` while `wildcard` wants `true` and `false`. And the DCV methods here are `http`, `txt`, `email` - no
`cname`, unlike the certificate-pack validation methods - with `http` recommended rather than `txt`.

**`custom_cert_bundle` needs one RSA and one ECDSA certificate.** That cannot be checked without parsing the
certificates, so the library checks what it can - one or two pairs, each with both halves - and leaves the
algorithm requirement to Cloudflare.

### The fallback origin

`/zones/{zone_id}/custom_hostnames/fallback_origin` - one origin for the whole zone, used by every custom
hostname that does not name a `custom_origin_server` of its own.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `GetFallbackOriginAsync(zoneId)` | `GET …/custom_hostnames/fallback_origin` | `{origin, status, created_at, updated_at, errors[]}`; `null` on 404 |
| `SetFallbackOriginAsync(UpdateCustomHostnameFallbackOriginRequest, zoneId)` | `PUT …/custom_hostnames/fallback_origin` | body is exactly `{"origin":"fallback.example.com"}` - one field, `PUT`, nothing to merge |
| `SetFallbackOriginAsync(origin, zoneId)` | `PUT …/custom_hostnames/fallback_origin` | the same call for the common case |
| `DeleteFallbackOriginAsync(zoneId)` | `DELETE …/custom_hostnames/fallback_origin` | leaves hostnames without their own origin nowhere to fall back to until one is set again |

**Read it back after setting it.** This response carries an `errors` list of its own, and it is **an array of
plain strings**, not the envelope's `{code, message}` objects: activation problems Cloudflare hit while
bringing the origin up, reported inside an otherwise successful call. A fallback origin can therefore be on
its way to `active` and broken at the same time. `HasErrors` says which, and `status` is one of six
(`initializing`, `pending_deployment`, `pending_deletion`, `active`, `deployment_timed_out`,
`deletion_timed_out`). Permissions are the same `SSL and Certificates Read`/`Write` as the hostnames
themselves.

Every endpoint in this resource family is wrapped now. The origin-side TLS resource
`origin_tls_client_auth` has its own section below: it is mutual TLS towards the origin, not part of SSL for
SaaS.

## Origin TLS client auth (mutual TLS to the origin)

`IOriginTlsClientAuthService` — `client.OriginTlsClientAuth`. The client certificate Cloudflare presents when
it connects to a zone's origin: `/zones/{zone_id}/origin_tls_client_auth`. A sibling of `ssl`, not part of it.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `GetSettingsAsync(zoneId)` | `GET …/origin_tls_client_auth/settings` | one boolean, `{enabled}` - no `id`, no `editable`, no `modified_on`, unlike the zone settings under `/zones/{zone_id}/settings` |
| `SetSettingsAsync(enabled, zoneId)` | `PUT …/origin_tls_client_auth/settings` | body `{"enabled":true\|false}`; the schema marks the flag required, so `false` is always sent explicitly |
| `GetHostnameAsync(hostname, zoneId)` | `GET …/origin_tls_client_auth/hostnames/{hostname}` | the association with **two** statuses: `status` for the association, `cert_status` for the certificate. `null` on 404 |
| `SetHostnamesAsync(UpdateOriginTlsHostnameAssociationsRequest, zoneId)` | `PUT …/origin_tls_client_auth/hostnames` | body `{"config":[{cert_id, enabled, hostname}, …]}` - the hostname travels in the **body**, and the response is a plain **array**, not a page |
| `EnableHostnameAsync` / `DisableHostnameAsync` / `VoidHostnameAsync(hostname, certificateId, zoneId)` | `PUT …/origin_tls_client_auth/hostnames` | the same call with one entry already built, one per state of `enabled` |
| `ListCertificatesAsync(zoneId)` | `GET …/origin_tls_client_auth/hostnames/certificates` | no filters and no paging: the whole collection. Ten per zone |
| `GetCertificateAsync(certificateId, zoneId)` | `GET …/hostnames/certificates/{certificate_id}` | `null` on 404 |
| `UploadCertificateAsync(UploadOriginTlsCertificateRequest, zoneId)` | `POST …/hostnames/certificates` | body `{certificate, private_key}`, both required |
| `UploadCertificateAsync(certificate, privateKey, zoneId)` | `POST …/hostnames/certificates` | the same call for the common case |
| `DeleteCertificateAsync(certificateId, zoneId)` | `DELETE …/hostnames/certificates/{certificate_id}` | **needs every association voided first** |
| `ListZoneCertificatesAsync(zoneId)` | `GET …/origin_tls_client_auth` | **deprecated**; returns the private key |
| `GetZoneCertificateAsync(certificateId, zoneId)` | `GET …/origin_tls_client_auth/{certificate_id}` | **deprecated**; `null` on 404 |
| `UploadZoneCertificateAsync(UploadOriginTlsCertificateRequest, zoneId)` | `POST …/origin_tls_client_auth` | **deprecated**; the same two-field body |
| `DeleteZoneCertificateAsync(certificateId, zoneId)` | `DELETE …/origin_tls_client_auth/{certificate_id}` | **deprecated** |

Permissions are `SSL and Certificates Read` to read and `SSL and Certificates Write` for everything else; the
certificate list accepts either.

**`enabled` has three states, and the third one is `null`.** `true` associates a hostname with a certificate
and turns it on. `false` keeps the association but stops the certificate being sent for that hostname - even
with the zone-level switch on - and it still counts towards the 100 hostnames one certificate may carry.
`null` **voids** the association, and Cloudflare requires exactly that before a certificate can be deleted, so
removing one is two calls: `VoidHostnameAsync`, then `DeleteCertificateAsync`. Because the shared JSON options
drop nulls, `OriginTlsHostnameAssociationChange.Enabled` is written out explicitly - otherwise the state that
matters most would be impossible to send.

**Three switches have to agree.** The zone-level setting, an enabled hostname association, and a certificate
that reached `active`. The zone switch on its own does nothing, and Cloudflare notes it may be set before or
after the upload, so the order of those two calls does not matter - but both are needed.

**The deprecated endpoints hand back the private key.** All four top-level methods - list, get, upload and
delete - return `private_key` in plaintext in the response body. `ZoneAuthenticatedOriginPull` models it
because it arrives, and its `ToString()` reports the key's length and never the key, following
`PagesUploadToken`; anything logging a response from these endpoints should be treated as holding a secret.
They are wrapped so an existing zone-level deployment can still be read and dismantled. Cloudflare superseded
them with the per-hostname resources, and a zone-level certificate overrides the shared one Cloudflare would
otherwise present, so an origin that does not trust it stops accepting connections from the edge - which is
why Cloudflare warns to keep exactly one active.

**Two neighbouring resources, two different status enums.** This one has seven values and includes `deleted`;
the custom hostname fallback origin's has six and does not. They are modelled separately
(`OriginTlsClientAuthStatuses`, `CustomHostnameFallbackOriginStatuses`) rather than sharing one list that
would be wrong for one of them.

**A path collision worth knowing.** `…/hostnames/{hostname}` and `…/hostnames/certificates` are siblings, so a
hostname literally called `certificates` would be indistinguishable from the certificate collection.

---

## Origin CA certificates

`IOriginCaCertificatesService` — `client.OriginCaCertificates`

An Origin CA certificate is one Cloudflare's own certificate authority signs for an **origin server**, so the
leg between Cloudflare's edge and the machine behind it is encrypted without a certificate from a public CA.
It is free on every plan, browsers do not trust it, and it is not meant to be served to them — it is what the
dashboard's *SSL/TLS → Origin Server* tab creates.

**The path is `/certificates`, at the API root — no account in it and no zone.** That is unlike every other
resource here, and the name of the resource suggests otherwise: a first draft of this wrapper sent
`/accounts/{account_id}/origin_ca_certificates`, and Cloudflare answered **404 `[10404] No route for that URI`**
to the `POST` as much as to the `GET`. Its own generated client and its published documentation both say
`/certificates`, so that is what this sends, and none of the four methods takes an account id. A `10404` is
worth remembering as its own kind of answer: it is Cloudflare saying the *path* does not exist, which is neither
a permission nor an entitlement, and no amount of scope on a token fixes it.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `CreateAsync(OriginCaCertificateRequest)` | `POST /certificates` | body is the CSR, the hostnames and the request type; `zone_id` too for a `keyless-certificate` request, which answers only `id`, `certificate` and `expires_on` |
| `ListAsync(zoneId, OriginCaCertificateListParameters?)` | `GET /certificates` | **`zone_id` is required** by the endpoint, so it is a parameter of the method rather than an optional filter; `page`, `per_page` bounded 5–50; revoked certificates are not included |
| `ListAllAsync(zoneId, OriginCaCertificateListParameters?)` | `GET /certificates` | `IAsyncEnumerable<OriginCaCertificate>`, walks every page and stops on `result_info` or on an empty one |
| `GetAsync(certificateId)` | `GET /certificates/{certificate_id}` | `certificateId` is the **x509 serial number**; null on 404 |
| `RevokeAsync(certificateId)` | `DELETE /certificates/{certificate_id}` | a revoke rather than a delete: answers `{id, revoked_at}` as `OriginCaCertificateRevocation` |

Five things about this resource shaped the wrapper:

- **The id is the certificate's x509 serial number**, not a UUID — Cloudflare documents it as such, and it is
  what the read and the revoke both take. `ToString()` calls it a serial number for the same reason.
- **The private key never comes near this library.** The caller generates a key and a Certificate Signing
  Request on the origin, sends the CSR here and gets a signed certificate back, so nothing that could decrypt
  the origin's traffic is ever transmitted. `System.Security.Cryptography.CertificateRequest` writes such a CSR
  in three lines — `CreateSigningRequest()`, then `PemEncoding.Write("CERTIFICATE REQUEST", …)` — and the live
  test does exactly that rather than carrying a fixture key around.
- **Both the CSR and the certificate are newline-encoded PEM**, and Cloudflare asks for `\n`. A CSR written on
  Windows carries `\r\n`, so the factories and `EnsureValid()` both settle the line endings — the validator
  changing the request is deliberate, because the service validates before it sends and a caller who set the
  properties by hand should still get a request Cloudflare accepts.
- **There is no update and no status.** A certificate is signed or it is not; `DELETE` is a *revoke*, and a
  revoked certificate drops out of the list rather than changing state. That makes the list the only place this
  resource says a revoke happened, and a read of one serial number the way to ask whether it is still trusted.
- **The list pages the ordinary way** — `page`/`per_page` with a `result_info` block — so it is a
  `CloudflarePage<T>` like most of the API, and not the bare array page rules answer.

Two things this library deliberately does not decide:

- `requested_validity` is an `int?` with the published day counts as constants in
  `OriginCaCertificateValidities` — 7, 30, 90, 365, 730, 1095, 5475 — rather than an enum, because the
  published set has changed over the life of this endpoint (older documentation offered 2190, 3650 and 7300 and
  none of the short ones), and refusing a number outside today's list would refuse a request Cloudflare might
  accept. A number that is not a length of time is still refused locally.
- The hostname grammar *is* checked locally, because Cloudflare publishes it exactly: fully qualified names
  belonging to zones on the account, with a wildcard allowed only as a single `*.` prefix over a multi-label
  domain. So `*.example.com` passes and `*.*.example.com`, `foo.*.example.com` and `*.com` are refused before a
  call is spent — and which zones a hostname belongs to is left to Cloudflare, which is the only party that
  knows.

**What a live run answered**, which is where four of the notes above come from and one of them was written
afterwards:

- `expires_on` arrives as **`2027-11-07 03:42:00 +0000 UTC`** — Go's default time layout, a space where ISO
  8601 puts a `T`, an offset with no colon in it, and the zone's name spelled out after it. `CloudflareTimestamps`
  did not read that shape and now does, which is the third way Cloudflare writes a timestamp; the same resource
  answers `revoked_at` in ISO 8601, so one endpoint carries two of the three.
- A revoked certificate **drops out of the list but still answers its own `GET`**. So the list is the place to
  ask whether a certificate is still trusted, and a read of one serial number is not — which matters to an
  origin that would otherwise treat a 200 as "still valid".
- The create answers `requested_validity`; the read of the same certificate afterwards does not. Both are
  nullable here, and neither is invented.
- The serial number that came back was **48 digits** — `147655896959702262542621997354679733308596274878` —
  which no integer type in .NET holds, so `Id` is a string and a caller must not try to make it a number.

No answer in the four calls carried a field this library does not model, so the model is complete for what this
account's plan answers.

Cloudflare also accepts a different credential on this path — an **Origin CA Key**, sent as
`X-Auth-User-Service-Key` — which this library has no notion of, because it sends an API token or the legacy key
and email everywhere. Its published documentation names no permission group of its own for the endpoint, so the
reach matrix probes `GET /certificates?zone_id=…` and a live run says which of the token's groups
reaches it.

Tests: `OriginCaCertificatesServiceTests` (13 facts) pin the exact create body, the settled line endings on both
paths that can carry a CSR, the `zone_id` the list cannot do without, the page walk, the tolerant 404, the
revocation answer, every way `EnsureValid()` says no at once, and a request type Cloudflare has not published
reading as `Unknown` on the way in while being refused on the way out. Live:
`An_origin_ca_certificate_can_be_requested_listed_read_and_revoked` generates a key inside the test, has
Cloudflare sign a CSR for a generated hostname in your zone for a year, reads the certificate back by its serial
number, revokes it, and checks that the list stopped naming it — revoking again in a `finally`, so a failure
mid-test cannot leave a signed certificate behind.

---

## mTLS certificates

`IMtlsCertificateService` — `client.MtlsCertificates`

The certificates an account uploads for Cloudflare to use with its mTLS-enabled services — **Bring Your Own CA**
being the ordinary one, where Cloudflare checks a client certificate against a CA the account supplied rather
than against one it issued. Certificates *issued by* Cloudflare's managed CA are a different resource
(`client_certificates`) on a different path, and are not covered here.

**The permission group is not the one the name suggests.** Cloudflare publishes two groups called *SSL and
Certificates*: the zone-scoped one behind certificate packs, Universal SSL, custom hostnames and the Origin CA
certificates above, and an **account-scoped** one whose own description is *"SSL MTLS certificates or
Certificate Store"*. These endpoints want the account-scoped one — Read `a7a233f9604845c787d4c8c39ac09c21`,
Write `db37e5f1cb1a4e1aabaef8deaea43575` — so a token holding only the zone-scoped group is refused here with a
403 that reads as though the group were missing when a group of that name is present under a different id.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(MtlsCertificateListParameters?, accountId)` | `GET /accounts/{account_id}/mtls_certificates` | a **bare array**, with no paging published. The one filter is `type`, and several types go in **one comma-separated parameter** — `?type=custom,gateway_managed` — rather than a repeated one |
| `GetAsync(certificateId, accountId)` | `GET …/mtls_certificates/{mtls_certificate_id}` | null when the account has none of that id |
| `CreateAsync(MtlsCertificateRequest, accountId)` | `POST …/mtls_certificates` | `ca` and `certificates` are both required; `name` exists for readability; `private_key` is only for the cases that need one — Cloudflare names Zero Trust's block page — and an ordinary BYO-CA upload never wants it. This answer, and only this one, carries `updated_at` |
| `DeleteAsync(certificateId, accountId)` | `DELETE …/mtls_certificates/{mtls_certificate_id}` | answers **the certificate it removed** rather than only its id, and null rather than a throw when there was nothing to remove |
| `ListAssociationsAsync(certificateId, accountId)` | `GET …/mtls_certificates/{mtls_certificate_id}/associations` | a bare array of `{service, status}` — the call to make *before* a delete, because it is what says whether removing the certificate would break a hostname or an Access application. Cloudflare publishes neither vocabulary, so both stay exactly as they arrive |

`MtlsCertificateRequest` settles line endings before it sends: **CRLF becomes LF** in the certificate and in the
key. PEM is a text format whose lines Cloudflare parses, a `\r` inside one is invisible in a log, and every
producer of PEM on Windows writes `\r\n` — so `EnsureValid()` rewrites them and says so in its remarks, which is
what makes it a deliberate mutation rather than a surprise. What it does *not* do is parse the certificate:
checking that the key matches it, or that it has not expired, would only mean two places to disagree, and an
expired CA is something an account may upload on purpose. It does check that both fields look like PEM at all,
because a DER blob or a file name where a certificate belongs is the mistake whose Cloudflare refusal —
"invalid certificate" — points somewhere unhelpful.

⛔ Not wrapped: `client_certificates` — the API Shield mTLS client certificates Cloudflare's managed CA
**issues** for a zone. They need the API Shield add-on, so they are out of scope with the other paid features
and a live run would only ever report an entitlement refusal for them. Also unwrapped: `acm`,
`keyless_certificates` and `certificate_authorities`.

---

## Users and accounts

`IUserService` — `client.User`; `IAccountsService` — `client.Accounts`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `User.VerifyTokenAsync()` | `GET /user/tokens/verify` | for a **user-owned** token (`cfut_...`); answers `1000 Invalid API Token` for an account-owned one |
| `User.VerifyAccountTokenAsync(string? accountId)` | `GET /accounts/{account_id}/tokens/verify` | for an **account-owned** token (`cfat_...`), the service principal CI should use |
| `User.ListPermissionGroupsAsync(name?, scope?)` | `GET /user/tokens/permission_groups` | every permission group a token may be granted: the **id** a token policy uses (a 32-character hex string, and *not* the short key the dashboard's `permissionGroupKeys` URL parameter takes), the name the dashboard shows, one of **13** categories, the scopes it applies to (account / zone / user / R2 bucket / one Worker script / one Flagship app - the last two only visible in the live account catalogue) and `is_selectable`, which is the difference between a group existing and this caller being allowed to hold it. Filters by `name` and `scope`. **User-scoped**: an account-owned token is refused with `9109` here, and the account-scoped sibling `GET /accounts/{account_id}/tokens/permission_groups` refuses it too, so CI cannot enumerate what it may be granted - which is why a refusal is traced to a permission group by hand |
| `User.ListAccountPermissionGroupsAsync(name?, scope?, accountId?)` | `GET /accounts/{account_id}/tokens/permission_groups` | the same rows, read from the catalogue **this account** can grant - and the one an account-owned token can actually answer: observed live at 200 with **415** groups, where the user-scoped endpoint refuses the same token with `9109`. These ids are what a token-creation URL for an account token takes in `permissionGroupKeys`, which is why the integration workflow builds its URL from them and cross-checks that list against this endpoint on every run that can read it. Cloudflare documents *API Tokens Read* as enough for the call, but a token holding only that was refused with `9109` and the one holding *API Tokens Write* as well answers. It is also the endpoint that taught the model its `description` field and the two per-resource scopes, none of which the published catalogue carries. Filters by `name` and `scope`; needs an account id, so it is refused locally without one |
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
| `SearchAsync(WorkerScriptSearchParameters?, accountId)` | `GET /accounts/{account_id}/workers/scripts-search` | its own endpoint rather than a filter on the list above, and page numbered with `result_info` where the list is cursor paginated, so `CloudflarePage<WorkerScriptSearchItem>` does describe it. `id` matches exactly only, `name` exactly and partially, `order_by` is `created_on`/`modified_on`/`name` and always ascending, `page` from 1 and `per_page` 1-100, both bounds-checked before sending. A row is a compact `{id, created_on, modified_on, script_name, environment_is_default, environment_name, service_name}`. `result_info.total_count` is documented as the total available **without** any search parameters, so it is the account's script count and not the size of the filtered result |
| `GetAsync(scriptName, accountId)` | `GET /accounts/{account_id}/workers/scripts/{script_name}` | **the script source as text**, `null` on 404 — Cloudflare documents this one as "fetch raw content for a Worker script … not JSON encoded", and their SDKs return a string. Metadata comes from `GetSettingsAsync` or the list |
| `GetContentAsync(scriptName, accountId)` | `GET …/workers/scripts/{script_name}/content/v2` | also raw text. The unversioned `…/content` path is the `PUT` that replaces content without touching config, so a `GET` there was never the documented endpoint |
| `ListVersionsAsync(scriptName, WorkerVersionListParameters?, accountId)` | `GET …/workers/scripts/{script_name}/versions` | newest first. Takes `page`, `per_page` and `deployable` (documented as ignoring pagination), but answers `{"items":[…]}` with **no** `result_info`, so it returns `WorkerVersionListResult` rather than `CloudflarePage<T>`. A version is `{id, number, metadata, annotations}`. The list page declines to name three of metadata's fields, but the versions model page publishes them: `source`, whose eleven values `WorkerVersionSources` lists, `modified_on`, and `hasPreview` - which a live answer spells `has_preview`, and that is the spelling the model reads. `annotations` is published by no version schema at all yet arrives anyway (`workers/triggered_by: upload`), and is the shared `WorkerAnnotations` type; an account-owned token uploads with an **empty** `author_email`. Anything still unnamed is captured in `AdditionalData`, and the integration test reports it only while there is something to report. **Propagation cuts both ways here**: a live run had this list answer *no versions at all* for a script that had answered 200 to both an upload and a version create moments earlier, and that had shown a version at the start of the same test — so the integration test reads the count five times over twenty seconds before asserting on it, and reports an answer that is still empty rather than failing on Cloudflare's propagation |
| `GetVersionAsync(scriptName, versionId, accountId)` | `GET …/workers/scripts/{script_name}/versions/{version_id}` | the list's `id`/`number`/`metadata` plus `resources` - that version's `bindings`, its `script` (`etag`, default `handlers`, `last_deployed_from`, `named_handlers`) and its `script_runtime` (`compatibility_date`, `compatibility_flags`, `limits.cpu_ms`, `migration_tag`, `usage_model`, `exports`). `null` on 404. The only call that shows a version's runtime as uploaded, since script settings describe the script rather than a version. `exports` stays a raw map: the reference types each entry as a union of six variants covering Durable Object lifecycle and transfer states, and the integration run reports what actually arrives instead of this library modelling it sight unseen |
| `CreateVersionAsync(scriptName, UploadWorkerScriptRequest, bool? bindingsInheritStrict, accountId)` | `POST …/workers/scripts/{script_name}/versions` | "Upload a Worker Version **without deploying** to Cloudflare's network" - the same `multipart/form-data` bundle as `UploadAsync` (one `metadata` part plus a part per module, built by the same private helper), but it creates a version and leaves the deployment serving what it was. **Module syntax only**: `main_module` is published as required here and there is no `body_part` alternative, so a service-worker-syntax request is rejected locally. `bindings_inherit=strict` turns an `inherit` binding that cannot be resolved against the latest uploaded version into error **10057** instead of letting Cloudflare silently drop it. Needs **Workers Scripts Write**, narrower than the read permission the other version endpoints accept. The response is the version in full plus two fields the version read does not publish: `startup_time_ms` and `exports_reconciliation`, the latter kept raw because all nine of its fields describe Durable Object migration outcomes this library cannot yet exercise. `metadata.annotations` is published here with **eight keys** - alias, tag, message and five CI details (commit SHA, repository URL, PR number/title/URL) - so `WorkerAnnotations` now carries those plus the `workers/triggered_by` only the deployment schema names; the maxima are documented but not enforced, because Cloudflare truncates a long `workers/message` rather than refusing it |
| `ListSchedulesAsync(scriptName, accountId)` | `GET …/workers/scripts/{script_name}/schedules` | the script's cron triggers. No query parameters, and `{"schedules":[{cron, created_on, modified_on}]}` - the array nested inside `result` with no `result_info`, the **third** Workers endpoint in a row that `CloudflarePage<T>` cannot describe. A script with no triggers answers with an empty array rather than an error |
| `UpdateSchedulesAsync(scriptName, IEnumerable<string> cronExpressions, accountId)` | `PUT …/workers/scripts/{script_name}/schedules` | **replaces the whole set** - a trigger not in the list is removed, so one expression sent to a script that has three leaves it with one. An empty list clears every trigger. The body is a **bare JSON array** of `{"cron": …}` objects rather than an object holding one, which is why the parameter is the expressions and not a request model. Needs **Workers Scripts Write** where the read needs only Read. Blank and duplicate expressions are refused locally; the grammar itself is left to Cloudflare, since guessing at it would reject expressions the API accepts. `created_on`/`modified_on` are Cloudflare's to write and are not sent. **An account prerequisite, not a permission**: with no `workers.dev` subdomain Cloudflare refuses the `PUT` with a 403 carrying **10063** "You need a workers.dev subdomain in order to proceed", even for a token whose Workers Scripts Write the same run has just proved by uploading and versioning a script. Opening Workers & Pages in the dashboard once creates the subdomain; the integration run reports 10063 rather than failing on it. Once the subdomain exists the whole flow is verified live: the `PUT` answers 200 with both triggers - **in an order of Cloudflare's choosing**, which is why the suite compares the set and reports the sequence it got rather than asserting one - the `GET` reads them back with `created_on` and `modified_on` filled in and nothing this library fails to model, and an empty `PUT` clears them again |
| `GetSettingsAsync(scriptName, accountId)` | `GET …/workers/scripts/{script_name}/script-settings` | **not** `/settings` — a common mistake |
| `UpdateSettingsAsync(scriptName, WorkerScriptSettings, accountId)` | `PATCH …/workers/scripts/{script_name}/script-settings` | changes `logpush`, `observability`, `tags`, `tail_consumers` - the same four fields the GET answers with, so one type serves both. **A patch, not a replace**: only what is set is sent, and this library never writes a JSON `null`, so clearing `tags` or `tail_consumers` means an empty list. A request with nothing set is refused locally (it would be an empty body). Observability now types `redact_query_string`, `issues`, `logs` (`enabled`, `invocation_logs`, `destinations`, `head_sampling_rate`, `persist`) and `traces` (plus `propagation_policy` ∈ `authenticated`, `accept`), which the published schema requires for a PATCH and previously landed in `AdditionalData`. |
| `CreateTailAsync(scriptName, accountId)` | `POST …/workers/scripts/{script_name}/tails` | starts a tail (a live stream of the Worker's logs and exceptions). **Takes no body** - the script in the path is the whole request. Answers `{id, expires_at, url}`, where `url` is what a consumer connects to - live, `wss://tail.developers.workers.dev/{id}`, a WebSocket endpoint rather than a REST one. The events themselves never pass through this API. **A script holds one tail at a time**: a second create answers 200 with a new id and replaces the running one. |
| `ListTailsAsync(scriptName, accountId)` | `GET …/workers/scripts/{script_name}/tails` | lists the script's tails, and answers **an array** - empty (`"result": []`) for a script that is not being tailed, and 404 reads as empty too. The published schema describes this endpoint as answering one object like the create does; a live call says otherwise. The items carry the same three fields the create answers with (`id`, `expires_at`, `url`), so one `WorkerTail` serves both and only the object-vs-array difference is real. |
| `DeleteTailAsync(scriptName, tailId, accountId)` | `DELETE …/workers/scripts/{script_name}/tails/{id}` | stops a tail early. A tail also stops on its own at `expires_at`, so deleting it is not the only way one ends. |
| `ListSecretsAsync(scriptName, accountId)` | `GET …/workers/scripts/{script_name}/secrets` | a bare array of `WorkerSecret`, no paging and no filters: **a name and a kind, never a value** - Cloudflare stores secrets encrypted and no read answers one. Both published kinds are one type: `secret_text` is just the name, `secret_key` adds `format`, `usages` and `algorithm` |
| `GetSecretAsync(scriptName, secretName, bool urlEncoded, accountId)` | `GET …/workers/scripts/{script_name}/secrets/{secret_name}` | one secret's name and kind; null when the script has no secret of that name. `url_encoded` is Cloudflare's flag saying the name in the path is percent-encoded, and is sent only when set |
| `PutSecretAsync(scriptName, WorkerSecretRequest, accountId)` | `PUT …/workers/scripts/{script_name}/secrets` | the body **is** the secret, unwrapped: `{name, type, text}` for a string, `{name, type, key_base64 or key_jwk, format, usages, algorithm}` for a key. Replaces any secret already stored under that name, and a request mixing the two kinds is refused locally. A `(scriptName, secretName, text)` overload covers the ordinary case. **The only call a secret's value ever travels on** |
| `DeleteSecretAsync(scriptName, secretName, bool urlEncoded, accountId)` | `DELETE …/workers/scripts/{script_name}/secrets/{secret_name}` | nothing modelled back - the answer is `null` |
| `UpdateSecretsBulkAsync(scriptName, WorkerSecretBulkRequest, accountId)` | `PATCH …/workers/scripts/{script_name}/secrets-bulk` | **`secrets-bulk`, not a sub-path of `secrets`**. One body writes several secrets and removes others - **a name mapped to `null` is a deletion**, which is why the type has its own converter: this library's writer omits nulls, and here that would turn a removal into nothing at all. Answers `{name: secret}`. Worth one call rather than several because each write makes a version of the script |
| `ListRoutesAsync(zoneId)` | `GET /zones/{zone_id}/workers/routes` | **zone-scoped**, unlike everything else in this service: a bare array of `WorkerRoute` (`id`, `pattern`, `script`, `request_limit_fail_open`), no paging and no filters. Needs **Workers Routes Read** |
| `ListRoutesForWorkerAsync(scriptName, zoneId)` | `GET /zones/{zone_id}/workers/routes` | the same call, filtered here - the endpoint takes no filter, so "which routes run this Worker" is a local answer, the way `ListDomainsForWorkerAsync` is. A route with no script belongs to no Worker |
| `GetRouteAsync(routeId, zoneId)` | `GET /zones/{zone_id}/workers/routes/{route_id}` | one route; null when the zone has no route of that id |
| `CreateRouteAsync(WorkerRouteRequest, zoneId)` | `POST /zones/{zone_id}/workers/routes` | the body is the route: `{pattern, script?}`. Needs **Workers Routes Write**. Leaving `script` out creates a route that runs nothing, which **negates the less specific patterns around it** - `WorkerRouteRequest.Bypassing(pattern)` says that in so many words, and `WorkerRoute.BypassesWorkers` reads it back |
| `UpdateRouteAsync(routeId, WorkerRouteRequest, zoneId)` | `PUT /zones/{zone_id}/workers/routes/{route_id}` | **a whole-route replacement**: a `script` left null takes the Worker off a route that had one. Read it first when only one of the two is meant to change |
| `DeleteRouteAsync(routeId, zoneId)` | `DELETE /zones/{zone_id}/workers/routes/{route_id}` | Cloudflare answers the id it removed and nothing else worth modelling |
| `GetAccountSettingsAsync(accountId)` | `GET /accounts/{account_id}/workers/account-settings` | the account-wide defaults - `default_usage_model` and `green_compute` - not one script's settings, which live on `…/script-settings` |
| `UpdateAccountSettingsAsync(WorkersAccountSettings, accountId)` | `PUT /accounts/{account_id}/workers/account-settings` | the same two fields, so a read can be handed straight back. Cloudflare documents this write as needing **Account Settings Write**, a group the Workers permissions do not include |
| `UploadAsync(UploadWorkerScriptRequest, accountId)` | `PUT …/workers/scripts/{script_name}` | `multipart/form-data`: a JSON `metadata` part plus one file part per module |
| `UploadAsync(scriptName, source, compatibilityDate, bindings, accountId)` | `PUT …/workers/scripts/{script_name}` | convenience overload for a single-module worker |
| `DeleteAsync(scriptName, accountId)` | `DELETE …/workers/scripts/{script_name}` | |
| `ListDeploymentsAsync(scriptName, WorkerDeploymentListParameters?, accountId)` | `GET …/workers/scripts/{script_name}/deployments` | newest first, and the first entry is the deployment actively serving traffic. Takes `page`, `per_page` (1–100) and an inclusive `since`/`until` creation-time range, all bounds-checked before sending. Answers `{"deployments":[…]}` **with** `result_info`, so the paging figures ride on `WorkerDeploymentListResult.Info` instead of a `CloudflarePage<T>`. A deployment is `{id, created_on, source, strategy, versions[{version_id, percentage}], annotations, author_email}`; `strategy` has one published value, `percentage`, and the percentages across a deployment's versions total 100 |
| `CreateDeploymentAsync(scriptName, CreateWorkerDeploymentRequest, accountId)` | `POST …/workers/scripts/{script_name}/deployments` | percentages must total exactly 100, checked locally in `decimal` before sending; `strategy` defaults to `percentage`; `annotations` is the shared typed `WorkerAnnotations` |
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

Verified live on a free-plan account: `PATCH …/script-settings` with `observability` (enabled, head sampling
rate, logs with invocation logs) was accepted, and the `GET` of the same path carried no field this library
does not model - so `WorkerScriptSettings` and `WorkerObservability` cover what Cloudflare actually sends.

A secret's value travels one way only. `WorkerSecret` has no property that could hold one, so a read cannot
leak a secret and nothing in this library logs one - `ToString()` is name and kind. `secret_key` is a WebCrypto
key rather than a string: `format` and `usages` are closed vocabularies, so they are enums, while `algorithm`
stays the JSON Cloudflare sent, because an algorithm object differs per algorithm the way WebCrypto's own do.
Secrets are covered by the **Workers Scripts Read and Write** permissions a script already needs, so no extra
group is wanted for them.

Routes are the one zone-scoped corner of this service, and their pattern grammar is Cloudflare's own rather
than a regex: `*` is the only operator, a pattern may begin with `http://` or `https://` (and matches both
schemes when it does not), a hostname may begin with `*` or `*.`, a path may end with `*`, query parameters may
not appear at all, and the pattern has to include the zone it is created in. `*example.com` matches any
hostname *ending* in `example.com` - `not-example.com` included - which is why matching only a zone and its
subdomains takes two routes. The most specific pattern wins when several match, the path component is case
sensitive, and the hostname is not for routes created on or after 15 October 2023. Only the shapes the
published rules rule out outright - no pattern, a query or fragment, whitespace - are refused locally; whether
a pattern belongs to the zone is Cloudflare's call, and it refuses one that does not and says why. A route also
needs a **proxied DNS record** for its hostname before any traffic reaches it, which is a DNS matter rather
than a routing one.

A route also answers **`request_limit_fail_open`** - whether a request that runs into the Worker's limits
reaches the origin instead of erroring - which no published schema for this endpoint mentions. A live run found
it on the create, the read and both writes, so it is a property now rather than an `AdditionalData` entry, and
it is read by the tolerant boolean converter because the kind Cloudflare writes it in is not published either.

The account settings are typed as the strings and flags Cloudflare publishes, with one deliberate exception:
`default_usage_model` stays a `string` because the endpoint's own documentation does not enumerate its words,
and `standard` - which `WorkerUsageModels` carries beside `bundled` and `unbound` - is not one of the words the
documentation mentions. An enum there would make a word this library has not met a hard failure on an account
that is working perfectly.

Verified live on a free-plan account: the route create, read, both writes and the delete all answered 200, the
`GET` of a deleted route answered "not there", `green_compute` arrived as a real boolean - so the settings read
parses as typed - and the settings **write** answered **403 `[10000]`** for a token without **Account Settings
Write**, exactly as its own documentation says. That group is now in the token URL the workflow generates, so
the next token created from it will let the write be verified too.

⛔ The newer JSON-only flow (`POST /workers/workers` → `…/versions` → `…/deployments`), Workers for
Platforms dispatch namespaces, and query filters on the domain list. Static asset uploads are wrapped too, in
*Workers static assets* below. Workers AI is wrapped - it has its own section below - and a script's secrets
are in the table above.

---

## Workers Observability

`IWorkersObservabilityService` — `client.WorkersObservability`

Telemetry destinations: where a Worker's logs, traces and metrics are exported to. They are **account-level**
resources, which is why they are their own service rather than more methods on the scripts one, and a Worker
reaches one through its own script settings - `observability.logs.destinations` and
`observability.traces.destinations` carry the slugs defined here.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `CreateDestinationAsync(CreateObservabilityDestinationRequest, accountId)` | `POST /accounts/{account_id}/workers/observability/destinations` | `name`, `enabled` and `configuration` are all required by the published schema, and `EnsureValid()` says so locally. The configuration is `{type: "logpush", url, headers, logpushDataset}` - the dataset is **fixed here**, because an update cannot change it. `skipPreflightCheck` skips Cloudflare's check that delivery actually works. `configuration.headers` is **required and has to be an object** - observed live, leaving it out answers 400 with `invalid_type` at `configuration.headers` expecting "a record", and the same answer echoed the camelCase field names back unchanged, so they are what the endpoint really spells them. `ForLogpush(name, url, dataset, enabled)` builds the whole thing, headers included as `{}` when there are none. **Verified live**: a create with `skipPreflightCheck` and a url that is not a real receiver was accepted, and the destination read back from the list with the slug the delete then addressed. |
| `UpdateDestinationAsync(slug, UpdateObservabilityDestinationRequest, accountId)` | `PATCH …/observability/destinations/{slug}` | `enabled` is **required on every update**, even when it is not the field changing, so an address or header change resends the flag it already has. The configuration takes `type`, `url`, `headers` and `replaceHeaders`; no dataset. Whenever a configuration is sent it is held to the same shape as a create's - `type`, `url` and `headers` all required - so changing only the address means resending the type and the headers too. **Verified live**: sending only `enabled` changes it and nothing is checked, but sending a `configuration` re-runs Cloudflare's delivery preflight and there is **no `skipPreflightCheck` here** - a destination whose url is not a real receiver answers 400 `Pre-flight check failed: Receiver returned HTTP 405` even when the url sent is the one it already has. A create can skip that check; an update cannot. |
| `ListDestinationsAsync(ObservabilityDestinationListParameters?, accountId)` | `GET …/observability/destinations` | one page, not a cursor. The filters are `order` (`asc`/`desc`), `orderBy` (`created`/`updated`), `page` and `perPage` - **camelCase on the wire**, where most Cloudflare endpoints spell them `per_page`, so the parameters class does not extend `CloudflarePagedParameters`. **Verified live**: `?order=desc&orderBy=created&page=1&perPage=5` answered 200. |
| `DeleteDestinationAsync(slug, accountId)` | `DELETE …/observability/destinations/{slug}` | answers with **the destination that was deleted**, not an empty success. |

All four answer with the same object: `slug`, `name`, `enabled`, `scripts` (the Workers exporting to it) and
`configuration`, where the response's configuration adds `jobStatus` (`error_message`, `last_complete`,
`last_error`) and its field names are camelCase (`logpushDataset`, `jobStatus`) around a snake_case job
status - so every property carries its wire name explicitly. `last_complete` and `last_error` are typed as
strings because the schema types them as strings and an empty value would otherwise be a parse failure.

Because this library never writes a JSON `null`, a header set to `null` is dropped rather than sent: removing
a header means `replaceHeaders: true` with the headers that should remain.

### Telemetry

The same service reaches the telemetry under `…/workers/observability/telemetry/`. Two of these are **lists
that are posted rather than got**, because what narrows them is a filter tree rather than a query string, and
their bodies are camelCase like the destinations' (`keyNeedle`, `scriptId`, `filterCombination`, `wsUrl`).

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListTelemetryKeysAsync(ObservabilityTelemetryKeysRequest?, accountId)` | `POST …/telemetry/keys` | the attribute keys that have appeared, each with its `type` and `lastSeenAt`. Null sends `{}`, which asks with no conditions. `datasets` empty means the defaults. |
| `ListTelemetryValuesAsync(ObservabilityTelemetryValuesRequest, accountId)` | `POST …/telemetry/values` | the values one key has been seen with. `key`, `type` and `timeframe` are required; `datasets` is **required to be present but may be empty**, which the schema documents as meaning the defaults - so leaving it out is refused locally as a different request from sending `[]`. |
| `QueryTelemetryAsync(ObservabilityTelemetryQueryRequest, accountId)` | `POST …/telemetry/query` | runs a query and answers with the run, its statistics, and whichever of six result shapes `view` asked for. `queryId` and `timeframe` are required: the id re-runs a query Cloudflare already holds when `parameters` is left out, and is any identifier when they are inline - `AdHoc(view, timeframe)` and `OverSpan(view, from, to)` generate one prefixed `cfnet-`. `dry` executes without persisting the results. **A distribution chart cannot be compared**: the schema says the pair answers 400, so `EnsureValid()` refuses it locally instead. Paging is a cursor (`offset`) for the event-shaped views and a numeric `offsetBy` for grouped ones. |
| `PrepareLiveTailAsync(ObservabilityLiveTailRequest?, accountId)` | `POST …/telemetry/live-tail` | prepares a WebSocket server and answers `{wsUrl}`. Opening it is the caller's part; a prepared session expires on its own and there is nothing to delete. |
| `SendLiveTailHeartbeatAsync(scriptId?, accountId)` | `POST …/telemetry/live-tail/heartbeat` | keeps that session alive. The published response type is `unknown`, so this is a void call: it either works or the session is gone. |

Filters are a union and modelled as one: `ObservabilityTelemetryFilterNode` is either an
`ObservabilityTelemetryFilter` leaf (`key`, `operation`, `type`, `value`) or an
`ObservabilityTelemetryFilterGroup` (`filterCombination`, `filters`), and the arm arrives as `kind` - which
the serializer writes, so a caller never sets it. Groups nest, and the schema caps that at
`ObservabilityTelemetryFilterNode.MaximumNestingDepth` (4), which the request validates locally. `value` is an
`object`, so a number reaches Cloudflare as a number rather than as a quoted one. The operations are on
`ObservabilityFilterOperations` - thirty spellings across three families, including symbolic ones like `>=` -
and are deliberately **not** validated against that list, because refusing one Cloudflare accepts would be
worse than passing it through.

`lastSeenAt`, `from` and `to` are epoch numbers whose unit the published schema does not state. A live run
settled it: a key seen that day reported `1793943698985`, so they are **milliseconds**, and
`ObservabilityTelemetryKey.LastSeen`, `ObservabilityTelemetryTimeframe.Between(DateTimeOffset, DateTimeOffset)`
and `ObservabilityTelemetryKeysRequest.OverSpan(from, to)` convert so a caller never has to. The same run read
keys like `$metadata.message`, and answered 0 values for one over a day on an account with no traffic - so an
empty answer there is a quiet account, not a broken call.

The query is one endpoint and six views, and which fields of the answer are populated moves with the view, so
`ObservabilityTelemetryQueryResult` carries all of them: `run` and `statistics` always arrive, then `events`,
`invocations`, `traces`, `agents`, `calculations` or `distribution` when one was asked for - plus `compare`,
which is the previous period of equal length in the calculations' own shape. Its `AllEvents`,
`AllInvocations`, `AllTraces`, `AllAgents` and `AllCalculations` read whichever came back as one list, so a
caller can print a result without first working out what it asked for.

Three of the schema's unions collapse instead of being modelled twice, because their arms are the same shape
with different fields required: an **invocation is an event** (`invocations` groups the very same objects by
request id), **`compare` is a `calculations` entry**, and the two arms of an event's `$workers` differ only in
whether `cpuTimeMs`, `wallTimeMs` and `outcome` are required. The echoed `run.query.parameters` is the one
place a request type is *not* reused: the request always writes a filter's `kind` discriminator, but the
schema makes it optional on a leaf, so an echo can carry a filter with no `kind` at all - and reading that
into the polymorphic tree would fail on a field Cloudflare merely left out.
`ObservabilityTelemetryRunQueryParameters` therefore keeps the echoed filters as raw `JsonElement`s and
shares every other type with the request. A live dry run echoed its one filter with the `kind` intact, and
the integration fact logs how many arrive that way; the raw reading stays, because one answer carrying it is
not the schema promising it.

An aggregate's numbers are estimates when the events behind them were sampled: `sampleInterval` is 1 when
nothing was and above 1 when `count` and `value` were extrapolated (`IsSampled` says which), `interval` is a
deprecated alias of it that Cloudflare still sends, and `statistics` - `bytes_read`, `rows_read`, `elapsed`
in seconds, `abr_level` - is the only part of the answer spelled in snake_case. The query's own schema also
settles the timestamp unit the keys endpoint left open, asking for "Unix timestamps in milliseconds", so
`ObservabilityTelemetryTimeframe` now converts both ways through `FromUtc` and `ToUtc`.

A calculation names its operator from `ObservabilityTelemetryCalculationOperators` - thirty-eight spellings
across the lowercase words and the uppercase ones the dashboard grew, `uniq` and `COUNT_DISTINCT` being the
same count in two cases - and is not validated against that list, for the reason a filter's operation is not.
A plain count needs no `key` and everything else does, which `EnsureValid()` checks. `groupBys`, `havings` and
`orderBy` only apply to the calculations view, and a having's operation *is* a closed set of six, so that one
is validated.

**Verified live**: a dry `events` query (one filter, one needle, `limit` 5) and a dry `calculations` query (a
`count` aliased `hits`, a `p99` over `$metadata.duration`, a group-by on `$metadata.service`, a having of
`hits > 0` and a descending ordering on `hits`, with `chart` and `granularity` 10) both answered **200** over
an hour's span, each with a `run` and its `statistics`. A **dry run completes synchronously** - `run.status`
came back `COMPLETED`, not `STARTED` - and the statistics are real work: an hour that matched nothing still
scanned `3609623 rows` and `28876984 bytes` in `0.101s`, because the platform's own telemetry sits in the
same tables. That is why a query is worth narrowing with a timeframe and a filter, and why the suite asserts
the scanned counts rather than the matched ones. The account has no traffic, so what came back for the views
themselves was empty - `0` events, `0` fields discovered - and the shape of an empty answer is still the
shape.

⛔ Unexercised live: an update that sends a destination `configuration`, which needs a receiver that answers
Cloudflare's preflight. The suite has none; a Worker of the account's own, uploaded and left answering 200,
would serve as one.

---

## Workers AI

`IWorkersAiService` — `client.WorkersAi`

Running a model, the catalogue behind it, and the fine-tunes and file formats beside them. These live under
`/accounts/{account_id}/ai/` — a **sibling** of
the Workers paths rather than one of them, even though the product is Workers AI. The three searches are
catalogues of what Cloudflare offers rather than of what the account owns, so read access is the whole of what
they need — and Cloudflare's own words for **Workers AI Read** are "grants access to invoke Workers AI
models", so `RunAsync` asks for no permission the catalogue did not. Nothing in this section is a paid
feature.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `RunAsync(model, WorkersAiRunRequest, accountId)` | `POST /accounts/{account_id}/ai/run/{model_name}` | one inference call. The model's inputs go in the body **directly** — no model/input/options wrapper — and which fields a model takes is what `GetModelSchemaAsync` says. Answers `WorkersAiRunResult`, which holds the JSON that arrived and reads the arm asked for. **The model's name goes into the path unescaped** - `…/ai/run/@cf/meta/llama-3.2-1b-instruct`, its slashes and its `@` intact: the escaped form this library uses for every other path segment was refused live with **400** and error **7000**, "No route for that URI". The name is therefore *checked* rather than escaped - whitespace, `?`, `#`, `%` and a leading or trailing slash are refused locally - and stays escaped where it is a query parameter, as in `GetModelSchemaAsync`. **Verified live** on both a generation and an embedding, which answered `usage.neurons` and `usage.prompt_tokens_details` and, for the embedding, `pooling` - three fields no published shape mentions and all three now modelled. See [Inference](#inference) |
| `ListModelsAsync(WorkersAiModelListParameters?, accountId)` | `GET /accounts/{account_id}/ai/models/search` | page-numbered, with `author`, `format`, `hide_experimental`, `include_deprecated`, `search`, `source` and `task` filters — all snake_case, and all optional, so no filters is no query string at all. `format=openrouter` asks for a marketplace-shaped answer (`{data: […]}`) which this library passes through but does **not** read: the default array is the only shape it types. **`task` takes the name a task list gives and nothing else**: a live `task=Text Generation` answered **39** models, each of them performing that task, where `task=text-generation` and `task=` the task's own `id` each answered an **empty page**. `total_count` reads **331** for all three and unfiltered alike, so it is not a count of what matched |
| `ListAllModelsAsync(...)` | `GET …/ai/models/search` | walks every page, keeping the filters and replacing only `page`; stops on a short page as well as on `result_info`, because a catalogue that fits in one page is the common case |
| `GetModelSchemaAsync(model, accountId)` | `GET …/ai/models/schema?model={model}` | the model's own input and output JSON Schema, which is the only published source for what an inference call takes — the shapes differ per task. `model` is the full name, namespace included (`@cf/meta/llama-3.2-3b-instruct`), so it goes out escaped here, where it is a query parameter - the same name is **not** escaped in `RunAsync`'s path, where Cloudflare's router wants it spelled out |
| `ListTasksAsync(accountId)` | `GET …/ai/tasks/search` | the task types — `text-generation`, `text-embeddings` and the rest, though **a live answer spells them for a person**, `Feature Extraction` rather than `feature-extraction`. That name - not the slug, and not the task's `id` - is what the model search's `task` filter takes, and `WorkersAiTaskNames.Same` bridges the two spellings for a caller comparing them. Takes **only** the account id: no filters at all, not even a search term, despite the path saying `search`. Answers a bare array |
| `ListAuthorsAsync(accountId)` | `GET …/ai/authors/search` | the authors and organisations behind the models, likewise a bare array with no filters. An author's name is what the model search's `author` filter takes - but **a live answer for this account carried no authors at all** (0 against a catalogue of 73 models), so that filter is worth trying before it is relied on, and the same is unresolved for `task` |
| `ListFinetunesAsync(accountId)` | `GET /accounts/{account_id}/ai/finetunes` | the account's own fine-tuning jobs, and it accepts **Workers AI Read** as well as Write. Its summary says it "lists all fine-tuning jobs created by the account, including status and metrics" while its published 200 example answers **one object** carrying neither, so both kinds are read: an array is the list, an object a list of one, and a null no jobs at all. `WorkersAiFinetune` names `id`, `name`, `model`, `description`, `public`, `created_at` and `modified_at`, and keeps anything else - the promised status and metrics first among them. **Verified live**: 200 for an account that has created no fine-tunes |
| `ListPublicFinetunesAsync(WorkersAiPublicFinetuneListParameters?, accountId)` | `GET /accounts/{account_id}/ai/finetunes/public` | the fine-tuned models Cloudflare publishes for every account, answering the same `WorkersAiFinetune` with `public` set. Paged by **`limit` and `offset`** rather than `page` and `per_page`, and ordered by **`orderBy`, camelCase on the wire**, so its parameters are their own class instead of this library's usual paging. **Verified live, and the published shape was wrong twice**: every item answered `"public": 1` - a number, not the boolean its shape declares - and both timestamps as `2024-05-08 02:23:55.897`, a space where ISO 8601 puts a `T` and no offset at all, the same form the model catalogue writes. So `Public` reads a boolean, a number or a word through `TolerantBooleanConverter`; the timestamps stay the strings that arrived, and `CreatedAtUtc` and `ModifiedAtUtc` read those through `CloudflareTimestamps`, which takes a timestamp with no offset to be UTC and brings one that has an offset to UTC as well |
| `ListToMarkdownFormatsAsync(accountId)` | `GET /accounts/{account_id}/ai/tomarkdown/supported` | the file formats the to-markdown conversion accepts: a bare array of `{extension, mimeType}` with no filters, and **`mimeType` camelCase**, which nearly nothing else Cloudflare answers is - so it is named explicitly rather than left to the snake_case policy. **Verified live**: 200, with nothing in an item this library does not model. `extension` arrives with its dot - `.html`, `.pdf` - as Cloudflare's own example writes it |
| `ConvertToMarkdownAsync(IEnumerable<WorkersAiToMarkdownFile>, accountId)` | `POST /accounts/{account_id}/ai/tomarkdown` | the conversion itself, and a **multipart/form-data** upload in which every part is named `files`, one per file - Cloudflare reads a file from its part's filename and content type rather than from anything in a body, so both go out on the part. It is the same `MultipartRequestBody` a Worker script upload sends. Answers a bare array of `WorkersAiToMarkdownConversion` - one entry per file, carrying `id`, `name`, `mimeType`, `format`, `data` and `tokens`. **A file that does not convert is not a failed call**: the answer is 200 with that entry's `format` as `error` and its `error` saying why, so `Succeeded` tells the two apart and `ToString()` prints the reason. `format` is what came *out* (`markdown`, or `error`) where `mimeType` is what went in. `tokens` is a **number** in Cloudflare's REST documentation and a **string** in its own generated client, so it stays the JSON that arrived and `TokenCount` reads either. `WorkersAiToMarkdownFile.OfText`, `.OfHtml` and `.OfBytes` make a file; a conversion with none in it, a file with no name, or a file with nothing in it is refused locally. **Verified live** - and the first attempt answered **400 with error 6005**, "Request body is not valid multipart/form-data", because .NET writes a part's `name` and `filename` bare whenever the value is a plain HTTP token, and this endpoint wants them quoted the way curl sends them. Every multipart part this library writes is quoted now, which is what Cloudflare's own examples show and what the Workers script upload accepts too |

**The published schema types the items of all three searches as `unknown`** — Cloudflare documents these
endpoints' query parameters and then answers with `result: array of unknown`, its own 200 example being
`[{}]`. So `WorkersAiModel`, `WorkersAiTask` and `WorkersAiAuthor` are deliberately narrow: they name the
fields the Workers AI documentation describes (`id`, `name`, `description`, `task`, `tags`, `properties`,
`limits`, `batching`) and preserve everything else in `AdditionalData`, which is the opposite of guessing —
a field this library has not named is reported by the integration run rather than silently dropped, and the
type is widened against a real answer. `WorkersAiModelProperty.Value` is the JSON it arrived as, because the
same property carries a boolean for `beta`, a string for `deprecation_date` and a number for
`context_window`.

`GET …/ai/models/schema` is published as three fields per side — `{additionalProperties, description, type}` —
and Cloudflare's own 200 example answers `"description": "JSON Schema definition for the model's input
parameters"` with `additionalProperties: true`, which reads as a description *of* a schema rather than the
schema the endpoint's summary promises ("use these definitions to determine the model-specific request fields
and response format"). **The summary is the honest one.** Asking for `@cf/pipecat-ai/smart-turn-v2` answered a
real, model-specific JSON Schema: an input whose `oneOf` offers audio either as a readable stream
(`{body, contentType}`) or as a base64 string, each with a `dtype` whose `enum` is `uint8`/`float32`/`float64`,
and an output carrying `contentType: application/json` with properties `is_complete` (boolean) and
`probability` (number). `WorkersAiJsonSchema` therefore models `properties`, `required`, `oneOf`, `enum` and
`contentType`, and **recurses**, because a schema nests. Enumerated values stay as the JSON they arrived as,
since a schema may enumerate numbers as readily as strings; `properties` keys are the model's own wire names
and are untouched by this library's snake_case policy, so `is_complete` reads as `is_complete`. `anyOf`,
`allOf`, `items` and `$schema` have not appeared in an answer, so they are deliberately not modelled — they
land in `AdditionalData`, exactly as anything Cloudflare adds next will.

**Verified live**: all three searches answered **200** and parsed, and the first three items of each carried
**no field this library does not model** — the deliberately narrow types match the catalogue as it is served,
not only as it is documented. The model search answered the catalogue's own first model,
`@cf/pipecat-ai/smart-turn-v2`, and `models/schema` answered 200 for it with the shape above. Both sides of
that answer carried **only `type`** of the three published fields, so `additionalProperties` and `description`
are optional in practice and the integration fact asserts no more than presence and a non-empty type.

A later run asked the same three endpoints harder questions. `tasks/search` answered **40** tasks whose names
are written for a person - the first reads `{"id":"eefd8baf-f3a5-4366-a03d-e53914f23ffb","name":"Feature
Extraction"}` and carries no `description`, though the published shape allows one; `authors/search` answered
**0** authors; and a model search filtered by that first task's name answered a `total_count` of **331** with
**nothing in the page**, where the same search unfiltered walked **73** models and found none of them
reporting the task name `text-generation` in any casing. That last count was the library's own doing: the
task names are written for a person, so matching `text-generation` against `Text Generation` needs the case
and the separators normalised, and once they are the same catalogue reports **39** generation models and **7**
embedding models across **10** task names (`Dumb Pipe`, `Text Generation`, `Text Embeddings`, `Text
Classification`, `Text-to-Image`, `Text-to-Speech` and four more).

It also answered two model fields no documentation mentions, which `WorkersAiModel` now names: **`source`**, a
number (`1` on every model) matching the search's own `source` parameter, and **`created_at`**, written
`2024-05-22 19:27:09.781` - a space where ISO 8601 puts a `T` and no offset, which is why it stays a string
rather than a `DateTime` that would have to guess. A model's `properties` are as varied as ever: `price`
arrives as a **list** of `{unit, price, currency}`, `reasoning_effort` as an object with its own
`supported_efforts`, and `context_window` as the *string* `"60000"`, so a property's value staying the JSON it
arrived as is not laziness.

The `task` filter is understood now, and it is the strangest thing here. All three spellings of one task
answer **200** with the same `total_count` of **331** - a number that is neither the 73 models an unfiltered
walk collects nor a count of what matched - but their pages differ completely: the name the task list gives,
`Text Generation`, answers **39** models every one of which performs that task, while the documentation's slug
`text-generation` and the task's own `id` each answer an **empty page**. So the filter works and only in the
catalogue's own spelling. `WorkersAiTaskNames.Normalise` and `.Same` reduce either spelling to what it says,
and `WorkersAiModel.PerformsTask` compares a model against a task through them - which is also the way to
filter locally, for a caller who would rather walk 73 models than depend on how a filter is spelled today. The
integration fact asserts all of it: the name filter's page is non-empty and every model in it performs the
task, the slug's and the id's pages are empty, and all three report the one `total_count`.

### Inference

`POST /accounts/{account_id}/ai/run/{model_name}` is one endpoint with fifteen published request shapes and
thirteen published answer shapes, one per task — and the model in the path, not the call, decides which.

**The request** is one class rather than fifteen. `WorkersAiRunRequest` carries every field of every arm and
sends only what is set, so the body is exactly the arm the model expects; the `ForPrompt`, `ForMessages`,
`ForEmbeddings`, `ForClassification`, `ForTranslation`, `ForSummarization`, `ForTextToImage`,
`ForTextToSpeech`, `ForSpeechRecognition`, `ForImageClassification`, `ForObjectDetection`, `ForImageToText`
and `ForMultimodalEmbeddings` factories set what a task requires and nothing else, and the knobs are then set
on the request. Three fields are published as two JSON kinds at once, and each is one property with a
converter that writes whichever it was given: `text` is a string or an array of them
(`WorkersAiTextOrBatch`), `image` is an array of 8-bit values or a base64 string (`WorkersAiImageInput`), and
a message's `content` is a string or an array of parts (`WorkersAiMessageContent`) — so a caller passes bytes
or base64, a text or a batch, and does not have to know which arm the model in the path is. `audio`, `image`
and `mask` are `List<byte>` rather than `byte[]` on purpose: System.Text.Json writes a `byte[]` as base64, and
these three go out as arrays of numbers.

`EnsureValid()` refuses what Cloudflare cannot act on — an empty body, a prompt and a text together, a prompt
and a conversation together, an empty conversation or batch — and enforces only the bounds Cloudflare
publishes (image height and width 256–2048, at most 20 diffusion steps, `strength` between 0 and 1). The
sampling knobs have no published range, so a strange `temperature` is sent rather than second-guessed.
**`stream` is refused locally**: a streamed answer is server-sent events, not the one JSON envelope this
library's transport reads. A field this library does not name goes out through `AdditionalData`, so a model's
own knob does not have to wait for a release of it.

**The answer** is `WorkersAiRunResult`: the JSON that arrived, plus a projection onto whichever arm is asked
for. An object arm reads as `AsTextGeneration()`, `AsEmbeddings()`, `AsTranscription()`, `AsAudio()`,
`AsImage()`, `AsTranslation()`, `AsSummarization()` or `AsImageDescription()`; an array arm as
`AsClassifications()` or `AsDetections()`; a bare string as itself. The projections guard on the kind that
arrived, so an arm that does not fit answers `null` or an empty list rather than throwing — `As<T>()` is the
unguarded escape hatch for an arm this library has not named. `Text` is the convenience most callers want: the
words, whichever field of whichever arm carries them (`response`, `text`, `translated_text`, `summary`,
`description`, or the whole answer when it is a string). Every arm keeps fields it does not name in
`AdditionalData`, and `WorkersAiToolCall.Arguments` stays raw JSON because Cloudflare publishes it as
`unknown` — a model answers the object its own tool declared.

**Two live answers widened it.** `@cf/meta/llama-3.2-1b-instruct` replied with a `usage` carrying
**`neurons: 0.18253148533403873`** and **`prompt_tokens_details: {"cached_tokens":0}`** — neither is
published, and the first is what a call actually costs, so `WorkersAiUsage` names both.
`@cf/baai/bge-base-en-v1.5` replied one vector of 768 with **`pooling`** and a **`usage`** of its own beside
the published `data` and `shape`, so `WorkersAiEmbeddings` names those as well. Both answers then left
`AdditionalData` empty, which is the point of naming them: what is left over next is what Cloudflare adds.

**The path is the one thing this endpoint does differently, and a live run is what said so**:
`POST …/ai/run/%40cf%2Fmeta%2Fllama-3.2-1b-instruct` — the model's name escaped the way every other path
segment in this library escapes — answered **400** with error **7000**, "No route for that URI". Cloudflare
routes inference on the name as it is spelled, so `RunAsync` writes `@cf/meta/llama-3.2-1b-instruct` into the
path intact and checks the name rather than escaping it.

**Verified live**: `POST …/ai/run/@cf/meta/llama-3.2-1b-instruct` with `{prompt, max_tokens: 8, temperature:
0}` answered **200** and `{"response":"What.","usage":{"completion_tokens":3,"prompt_tokens":52,
"total_tokens":55,…}}`; `POST …/ai/run/@cf/baai/bge-base-en-v1.5` with `{"text":"hello"}` answered **200**
with `shape: [1,768]` and the vector itself. Both were addressed by the name spelled out — the escaped form
never reached a model at all.

⛔ `POST …/ai/finetunes` and `POST …/ai/finetunes/{finetune_id}/finetune-assets`: both need **Workers AI
Write**, which the token template here does not ask for, and creating a fine-tune starts billable training, so
they belong to the paid half of the product rather than to the next increment. Wrapping them is no structural
problem — the asset upload is a multipart form, and `MultipartRequestBody` is what `ConvertToMarkdownAsync` and
a Worker script upload already send — it is only that nothing here could verify them against a live account.
`conversionOptions`, the optional JSON
form field that goes beside the files in a to-markdown conversion, is not wrapped either: its options are
documented on a page of their own and nothing here has read them yet, so sending one would be a guess. The AI
Gateway is a resource of its own, not part of this one. Streaming inference is not wrapped either, and would
need a transport that reads server-sent events rather than one envelope.

---

## Pages

`IPagesProjectsService` — `client.Pages`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(PagesProjectListParameters?, accountId)` | `GET /accounts/{account_id}/pages/projects` | sends `page`, `per_page` (always paired — Pages answers a bare page size with `8000024`) and `search`; retries once with no query at all when Cloudflare answers `8000024 Invalid list options provided`, which it does for some accounts even though the schema documents both paging parameters. `search` is not in the published schema and is only sent when set. **A project connected to a repository is what makes this endpoint dangerous to model**: its `latest_deployment` carries `source` as an object (`{"type":"github","config":{…}}`) and `deployment_trigger` as an object (`{"type":"github:push","metadata":{…}}`). A live run caught `source` being read as a string, which failed the whole list - and so every Pages test - on one field of one project. Only visible on an account that has such a project: a Direct Upload project has neither |
| `ListAllAsync(...)` | `GET …/pages/projects` | walks every page |
| `GetAsync(projectName, accountId)` | `GET …/pages/projects/{project_name}` | `null` on 404 |
| `CreateAsync(string projectName, string productionBranch, accountId)` | `POST …/pages/projects` | |
| `CreateAsync(CreatePagesProjectRequest, accountId)` | `POST …/pages/projects` | validates the project name locally (≤58 chars, lowercase/digit start, `[a-z0-9-]`) |
| `UpdateAsync(projectName, UpdatePagesProjectRequest, accountId)` | `PATCH …/pages/projects/{project_name}` | |
| `PurgeBuildCacheAsync(projectName, accountId)` | `POST …/pages/projects/{project_name}/purge_build_cache` | no body, no meaningful result - the next build starts from an empty cache |
| `GetUploadTokenAsync(projectName, accountId)` | `GET …/pages/projects/{project_name}/upload-token` | `{jwt}` - the short-lived token the asset endpoints want instead of your API token. `ToString()` reports its length, never the token |
| `DeleteAsync(projectName, accountId)` | `DELETE …/pages/projects/{project_name}` | |
| `ListDeploymentsAsync(projectName, PagesDeploymentListParameters?, accountId)` | `GET …/pages/projects/{project_name}/deployments` | the schema documents `env` (`production`/`preview`), `page` and `per_page`; `branch`, `order` and `direction` are not in it and are only sent when set, the same caveat the project list carries for `search` |
| `GetDeploymentAsync(projectName, deploymentId, accountId)` | `GET …/deployments/{deployment_id}` | `null` on 404 |
| `CreateDeploymentAsync(projectName, CreatePagesDeploymentRequest?, accountId)` | `POST …/deployments` | direct-upload deployment |
| `DeleteDeploymentAsync(projectName, deploymentId, accountId)` | `DELETE …/deployments/{deployment_id}` | |
| `RetryDeploymentAsync(projectName, deploymentId, accountId)` | `POST …/deployments/{deployment_id}/retry` | no body; runs the build again, which is what a deployment that failed on something transient needs. Distinct from rollback, which points production at a build that already succeeded |
| `GetDeploymentLogsAsync(projectName, deploymentId, accountId)` | `GET …/deployments/{deployment_id}/history/logs` | `{data:[{line,ts}], includes_container_logs, total}`. `Text` joins the lines for printing; `total` can exceed what came back, so a truncated log is not an error. The only way to read a failed build's output through the API |
| `RollbackDeploymentAsync(projectName, deploymentId, accountId)` | `POST …/deployments/{deployment_id}/rollback` | rolls **production** back to the deployment in the path; no body, returns the deployment now serving |
| `ListDomainsAsync(projectName, PagesDomainListParameters?, accountId)` | `GET …/pages/projects/{project_name}/domains` | |
| `GetDomainAsync(projectName, domainName, accountId)` | `GET …/domains/{domain}` | `null` on 404 |
| `AddDomainAsync(projectName, domainName, accountId)` | `POST …/domains` | |
| `UpdateDomainAsync(projectName, domainName, UpdatePagesDomainRequest, accountId)` | `PATCH …/domains/{domain}` | `verified`, `ownership_verified` |
| `DeleteDomainAsync(projectName, domainName, accountId)` | `DELETE …/domains/{domain}` | |

`PagesBuildConfig`, `PagesDeploymentConfigs` (`preview` + `production`, ~21 fields each),
`PagesProjectSource` (with its `PagesSourceConfig` repository details), `PagesDeploymentTrigger`,
`PagesEnvVar` and the Pages binding dictionaries are modelled; anything Cloudflare adds later is
preserved in `AdditionalData`. `deployment_trigger.metadata` is typed from a live payload - an `ad_hoc`
trigger sent `branch`, `commit_hash`, `commit_message` and `commit_dirty` - so a `github:push` or
`deploy_hook` trigger carrying more is reported by the integration run rather than silently kept. One
deliberate leftover: `PagesDeployment.TriggerSource` is kept although the published schema does not carry
it, because a property Cloudflare never sends stays null and costs nothing, whereas dropping one it does
send loses data.

**Pushing the assets themselves is deliberately not wrapped, and the reason matters.** Direct Upload is four
calls: `GET …/upload-token` for a short-lived JWT, then `POST /client/v4/pages/assets/check-missing`,
`/upload` and `/upsert-hashes` authenticated *with that JWT* rather than your API token, then
`POST …/deployments` with the manifest. Only the first and the last are in the published API reference; the
three asset endpoints are not, they carry no `/accounts/{id}` prefix, and they are keyed by a hash that is
BLAKE3 over the file's **base64 text plus its extension without the dot** - not SHA-256 of the bytes. Getting
that wrong is silent: the upload succeeds, the upsert succeeds, the deployment succeeds, `file_count` comes
back positive, and every asset 404s forever. A wrapper that cannot be verified offline and fails without an
error is worse than no wrapper, so this library hands over the token (`GetUploadTokenAsync`) and leaves the
bytes to wrangler, which is the supported path.

**There is no Functions-routes, `_headers` or `_redirects` endpoint to wrap**, and this line used to promise
one. All three are files rather than resources: `_headers` and `_redirects` are plain-text files in the build
output, and a Function's routes come from the `/functions` directory, with `_routes.json` generated from it
(`version`, `include`, `exclude`, where an exclude always wins). The Pages resource exposes projects,
deployments, deployments history logs and domains and nothing else, so the only API-level view of those files
is the assets they were built into. Two consequences worth keeping: a redirect set larger than `_redirects`
allows (2,000 static plus 100 dynamic, each declaration under 1,000 characters) belongs in **Bulk Redirects**,
which this library does not wrap yet; and neither file applies to a response a Pages Function generated, so a
Function sets its own headers on the `Response` it returns.

⛔ Build logs and the deployment lifecycle are covered above; Bulk Redirects is the real remaining gap.

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

## Lists (account rule lists)

`IRulesListService` — `client.Lists`

Lists are the account-scoped sets of values that rules match against: IPs, hostnames, ASNs, and the
source/target URL pairs behind Bulk Redirects. A list's `kind` is fixed when it is created and decides
which one value field its items populate; `RulesListItem` declares all four, so a mixed account can be read
with one model and `ValueKind` says which one is populated.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(accountId)` | `GET /accounts/{account_id}/rules/lists` | the whole set at once - no `result_info`, so there is nothing to page. Each row carries `num_items` and `num_referencing_filters`, the latter being how many rules refer to the list by name |
| `GetAsync(listId, accountId)` | `GET …/rules/lists/{list_id}` | `null` on 404 |
| `CreateAsync(CreateRulesListRequest, accountId)` | `POST …/rules/lists` | body is `name`, `kind`, `description`. A missing name or kind is refused locally, because the kind cannot be changed afterwards |
| `UpdateAsync(listId, UpdateRulesListRequest, accountId)` | `PUT …/rules/lists/{list_id}` | a **PUT**, not a PATCH, and the description is the whole of what it accepts |
| `DeleteAsync(listId, accountId)` | `DELETE …/rules/lists/{list_id}` | takes every item with it and returns the id Cloudflare echoes. A list a rule still refers to is refused rather than deleted |
| `ListItemsAsync(listId, RulesListItemListParameters?, accountId)` | `GET …/rules/lists/{list_id}/items` | cursor paged: `after`, `per_page`, `search`. `NextCursor` is what the next call takes; the response's cursors also carry a `before`, which the endpoint does not accept |
| `GetItemAsync(listId, itemId, accountId)` | `GET …/rules/lists/{list_id}/items/{item_id}` | `null` on 404 |
| `CreateItemsAsync(listId, items, accountId)` | `POST …/rules/lists/{list_id}/items` | a **bare JSON array** body, answered with `{"operation_id": …}`. Refused locally for an empty batch, an item carrying no value, or a batch mixing kinds |
| `ReplaceItemsAsync(listId, items, accountId)` | `PUT …/rules/lists/{list_id}/items` | replaces the whole set; an empty array clears the list, which is the one call allowed to send one |
| `DeleteItemsAsync(listId, itemIds, accountId)` | `DELETE …/rules/lists/{list_id}/items` | a DELETE **with a body**: `{"items":[{"id":…}]}`, ids trimmed and de-duplicated. Refused locally with no ids, and the message points at `ReplaceItemsAsync` for clearing |
| `GetBulkOperationAsync(operationId, accountId)` | `GET …/rules/lists/bulk_operations/{operation_id}` | `null` on 404, which is also what an operation that has aged out looks like. One model reads all three shapes: pending/running, completed (adds `completed`), failed (adds `error`) |
| `WaitForBulkOperationAsync(operationId, timeout?, pollInterval?, accountId)` | the same endpoint, polled | stops as soon as `IsFinished`, a failure included, and returns the last state rather than throwing when the time runs out. An unknown id raises instead of polling until the timeout |

Redirect entries (`RulesListRedirect`) carry `source_url`, `target_url`, `status_code` (301, 302, 307 or
308) and the four flags the dashboard shows as toggles - `include_subdomains`, `preserve_query_string`,
`preserve_path_suffix`, `subpath_matching`. Each flag is nullable and omitted when unset, so Cloudflare's
own default applies instead of a `false` this library invented. Hostname entries carry `url_hostname` and
`exclude_exact_hostname`, which matches subdomains but not the name itself.

The rule that turns one of these lists into actual redirects is in the next section: a ruleset in the
`http_request_redirect` phase with a `from_list` action parameter naming the list.

---

## Rulesets (the ruleset engine)

`IRulesetService` — `client.Rulesets`

A ruleset is an ordered set of rules Cloudflare runs at one phase of the request lifecycle. The API serves
the identical resource from an account path and a zone path - its own schema spells that
`{accounts_or_zones}` - so **every method takes both an `accountId` and a `zoneId`** and works against the
zone only when a zone id is passed explicitly. The configured default `ZoneId` is deliberately not
consulted, because a method that changed scope depending on how the client was configured would be worse
than one that asks.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(RulesetListParameters?, accountId, zoneId)` | `GET {accounts_or_zones}/{id}/rulesets` | cursor paged; no kind or phase filter exists, so narrowing is the caller's job. Rules are not included in a list |
| `GetAsync(rulesetId, accountId, zoneId)` | `GET …/rulesets/{ruleset_id}` | `null` on 404. This is the call that brings the rules back |
| `CreateAsync(CreateRulesetRequest, dryRun?, accountId, zoneId)` | `POST …/rulesets` | `name`, `kind`, `phase` and optionally `rules`, so a ruleset and its first rules land in one version. A missing name, kind or phase is refused locally. `kind: custom` is inert until a root ruleset executes it |
| `UpdateAsync(rulesetId, UpdateRulesetRequest, dryRun?, …)` | `PUT …/rulesets/{ruleset_id}` | a **whole-ruleset replacement**: omitting `rules` removes them all, so a request with nothing set is refused locally and an empty `Rules` list is how you clear them on purpose |
| `DeleteAsync(rulesetId, dryRun?, …)` | `DELETE …/rulesets/{ruleset_id}` | |
| `GetEntrypointAsync(phase, accountId, zoneId)` | `GET …/rulesets/phases/{phase}/entrypoint` | `null` when the scope has no root ruleset for that phase, which is the normal state for an unconfigured one |
| `UpdateEntrypointAsync(phase, UpdateRulesetRequest, dryRun?, …)` | `PUT …/rulesets/phases/{phase}/entrypoint` | changes what live traffic does; read it first |
| `CreateRuleAsync(rulesetId, CreateRulesetRuleRequest, dryRun?, …)` | `POST …/rulesets/{ruleset_id}/rules` | appends. **Answers with the whole ruleset**, not the rule |
| `EditRuleAsync(rulesetId, ruleId, CreateRulesetRuleRequest, dryRun?, …)` | `PATCH …/rulesets/{ruleset_id}/rules/{rule_id}` | not the sparse patch it looks like: Cloudflare requires the **action and the expression** on an edit too (`20015`/`20125` observed live), so the rule goes back whole with what changed. Other unset fields are left alone, and an edit missing either is refused locally |
| `DeleteRuleAsync(rulesetId, ruleId, dryRun?, …)` | `DELETE …/rulesets/{ruleset_id}/rules/{rule_id}` | answers with the ruleset as it now stands |
| `ListVersionsAsync(rulesetId, …)` | `GET …/rulesets/{ruleset_id}/versions` | a version is a frozen ruleset, so it reads back through the same model |
| `GetVersionAsync(rulesetId, version, …)` | `GET …/rulesets/{ruleset_id}/versions/{version}` | `null` on 404, which is what a pruned version looks like |
| `DeleteVersionAsync(rulesetId, version, …)` | `DELETE …/rulesets/{ruleset_id}/versions/{version}` | prunes one; the version in force and the one before it cannot be pruned |

`Ruleset.Version` is a **string** in Cloudflare's own schema even though it reads like a number, and every
change makes a new one. `RulesetKind` covers `root`, `managed`, `custom` and `zone`; `RulesetPhase` covers
the 23 phases the API publishes, from `http_request_redirect` (Bulk Redirects) through
`http_request_firewall_custom` (custom WAF rules) to `magic_transit`.

Four things the live API taught, which its docs do not lead with:

- `kind: custom` is refused for the `http_request_redirect` phase with `20004`, so Bulk Redirects lives in
  the account's **root** ruleset for that phase rather than in a ruleset of your own. Custom rulesets are
  documented for `http_request_firewall_custom`, and at zone level only for that phase at all.
- Writing a ruleset also needs the phase to be entitled: this account answers `50002`, "not entitled to use
  the phase", for `http_request_firewall_custom`. The integration suite therefore writes a root redirect
  ruleset only where the account runs none, and otherwise validates its rule against the live entrypoint
  with `dry_run=true` and then checks the version did not move.
- A managed rule may carry no `expression` at all - Cloudflare defines it by `ref` - and a managed ruleset
  says where it came from in `source`, observed live as `firewall_managed`.
- `from_list.key` is **not** the name of a list field, which is what it looks like: Cloudflare parses it as
  a Wirefilter expression naming the part of the request to look up among the list's source URLs, and
  `target_url` there is refused with `20082`, "could not parse filter value expression". The documented
  value is `http.request.full_uri`, and the rule's own `expression` has to reference the same field -
  normally `http.request.full_uri in $<list name>`. That pair is what
  `CreateRulesetRuleRequest.ForRedirectFromList` builds, which is why it takes a key rather than letting
  the caller set one half of it.

Rules come in twenty-odd action-specific shapes. `RulesetRule` declares the fields every rule shares, plus
the **redirect** parameters - `from_list` (`name` + `key`, where the key is an expression, the Bulk
Redirects case, built by `CreateRulesetRuleRequest.ForRedirectFromList`) and `from_value` (a literal or expression target with
`status_code` and `preserve_query_string`, the Dynamic Redirect case). Any other action's parameters arrive
in `RulesetActionParameters.AdditionalData` as they came rather than being guessed at, and the integration
test reports which ones those were so a later release can type them from evidence.

⛔ Not wrapped yet: `POST …/rulesets/{ruleset_id}/import` (importing a whole set of rules), the managed
ruleset overrides an `execute` rule carries, and rate limit rule parameters.

---

## Firewall filters and IP Access rules

`IFirewallService` — `client.Firewall`

Two halves of the firewall that the ruleset engine did not absorb, and they are not alike. A **filter** is a
saved wirefilter expression in a zone that a firewall rule then points at, and Cloudflare documents the create
as **deprecated in favour of the Ruleset Engine** — a zone whose rules have all been migrated has no reason to
make one, which is why `client.Rulesets` is where new work belongs. An **IP Access rule** is current: it says
what to do with requests from one address, range, ASN or country — block them, challenge them, or skip the rest
of the firewall for them.

The two halves do not share a permission group either. Filters ride on **Firewall Services**; IP Access rules
need **Account Firewall Access Rules**, a group of their own, so a token that can read a zone's filters can
still be refused on the rules. That is the one new permission this service asks for.

**Saved filters** — deprecated, still served, one zone at a time.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListFiltersAsync(FirewallFilterListParameters?, zoneId)` | `GET /zones/{zone_id}/filters` | paged; a filter per field — `id`, `description`, `expression`, `paused`, `ref` — each an exact match on the value |
| `ListAllFiltersAsync(FirewallFilterListParameters?, zoneId)` | the same, walked | one async sequence over every page, stopping when Cloudflare stops reporting more |
| `GetFilterAsync(filterId, zoneId)` | `GET /zones/{zone_id}/filters/{filter_id}` | null when the zone has no filter of that id |
| `CreateFiltersAsync(filters, zoneId)` | `POST /zones/{zone_id}/filters` | the body is a **bare array** and there is no single-filter create, so one filter is an array of one; Cloudflare assigns the `id` |
| `UpdateFilterAsync(filterId, filter, zoneId)` | `PUT /zones/{zone_id}/filters/{filter_id}` | replaces the filter, so a field left out is a field removed; refuses a body whose `id` names a different filter than the path does |
| `UpdateFiltersAsync(filters, zoneId)` | `PUT /zones/{zone_id}/filters` | the bulk form: the same array with an `id` on each member, which is what says which filter changes |
| `DeleteFilterAsync(filterId, zoneId)` | `DELETE /zones/{zone_id}/filters/{filter_id}` | answers `{"id": …}`; null rather than a throw when the filter is already gone |
| `DeleteFiltersAsync(filterIds, zoneId)` | `DELETE /zones/{zone_id}/filters` | a **`DELETE` with a body** — `{"id":["…"]}`, a singular wire name for a list |

`FirewallFilterRequest` is what all three writes take. `For(expression, description)` starts one, `WithId`
makes the bulk form of it, `FromFilter` copies a filter that was read so a bulk update can change one field and
keep the rest, and `SetPaused()` / `Tagged(ref)` fill in the other two. `EnsureValid()` refuses a filter with no
expression — one that matches nothing does nothing — and checks the description against the **500** characters
and the `ref` against the **50** that Cloudflare publishes. It does not check the `id`'s length on the way in:
Cloudflare says an id is exactly 32 characters, and a locally enforced minimum would refuse an id Cloudflare is
willing to accept. `expression` is wirefilter and is passed through untouched, so what the firewall can express
is never narrower than what this library knows about.

That `DELETE` with a body is why `CloudflareRequest.Delete` has an overload taking a payload. It is the only
one in the API this library wraps: every other delete carries its identifiers in the path or in the query.

**IP Access rules** — current, and served from two scopes.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAccessRulesAsync(AccessRuleListParameters?, accountId, zoneId)` | `GET /{accounts\|zones}/{id}/firewall/access_rules/rules` | paged. One filter is **nested** and goes out as dotted names — `configuration.target` and `configuration.value` — beside `mode`, `notes`, `order` ∈ `configuration.target`/`configuration.value`/`mode`, `direction` and `match` |
| `GetAccessRuleAsync(ruleId, accountId, zoneId)` | `GET …/rules/{rule_id}` | null when that scope holds no rule of that id |
| `CreateAccessRuleAsync(AccessRuleRequest, accountId, zoneId)` | `POST …/rules` | `configuration` and `mode` are both required, `notes` optional |
| `EditAccessRuleAsync(ruleId, AccessRuleRequest, accountId, zoneId)` | `PATCH …/rules/{rule_id}` | spelled `PATCH`, behaving like a `PUT`: the published schema requires the configuration **and** the mode here, so there is no changing one of them on its own |
| `DeleteAccessRuleAsync(ruleId, accountId, zoneId)` | `DELETE …/rules/{rule_id}` | answers `{"id": …}`; null rather than a throw when the rule is already gone |

**Scope** is the same pair of parameters `client.Rulesets` uses, and the zone wins — but only when it is passed
explicitly. A client configured with a default zone does *not* quietly move an access rule call from the account
path to the zone one, because the two answers mean different things: a rule under the account applies to every
zone in it, a rule under a zone applies to that zone alone. Pass the zone to get the zone.

`AccessRuleConfiguration` covers the five targets with one type and one factory each — `ForIp`, `ForIpv6`,
`ForRange`, `ForAsn`, `ForCountry`. `EnsureValid()` enforces what Cloudflare publishes rather than what looks
plausible: a range takes a prefix length of **/16 or /24** for IPv4 and **/32, /48 or /64** for IPv6, so
`192.0.2.0/23` is refused here instead of being refused there; a country target takes an **ISO-3166-1 alpha-2**
code, which is why `GBR` is refused — three letters is the alpha-3 code from a different standard.

`mode` ∈ `block`, `challenge`, `whitelist`, `js_challenge`, `managed_challenge`. **`whitelist` is the word the
API still uses for what the dashboard calls *skip*** — a rule that lets its addresses past the rest of the
firewall — so the member is named `AccessRuleMode.Whitelist` for the wire and documented as the skip. Cloudflare
renamed it in the product and not in the API, and sending `skip` would be refused.

A created rule answers `allowed_modes`, Cloudflare's own list of the modes that rule may be switched to, which
depends on the plan. `AccessRule.Allows(mode)` reads it, so a change can ask for a mode the account will accept
rather than guessing at one — the live fact does exactly that instead of hardcoding `challenge`.

One asymmetry is worth knowing before you write a query. The **list's filter vocabulary is shorter than the
rule's**: Cloudflare types the list's `configuration.target` separately and leaves `ip6` out of it, so there is
no way to narrow a list to a single IPv6 address. That is why `AccessRuleListConfiguration.Of` takes an
`AccessRuleFilterTarget` — four values — rather than the `AccessRuleTarget` a rule carries, five.

Both enums are tolerant on the way in and strict on the way out, like every enum here: a word this version has
never met reads as `Unknown` and prints as *"an unpublished mode"*, and writing one throws rather than sending
Cloudflare a word it has not published.

**What a live run found.** Three things, each of them the kind a published schema does not tell you.

- **The filters API is switched off for at least some accounts.** `POST /zones/{zone_id}/filters` answered
  **`410 Gone`, error `10020`, `filters.api.deprecated`** — *"This API has been deprecated. Please use the
  Rulesets API instead."* The endpoint is still published and still wrapped here, because an account whose
  rules were never migrated may still be served it, but the live fact opts out with that reason instead of
  failing, and the reach matrix files a `410` under *"refused but not a permission problem"*: no token reaches
  a retired endpoint, and hunting for a permission group would be a waste of an afternoon.
- **The two access rule paths do not share a permission requirement.** One token created a rule on the **zone**
  path, listed it back from the same path, and was then refused on
  `GET /accounts/{account_id}/firewall/access_rules/rules` with `10000`. The reference names a single group for
  the resource — **Account Firewall Access Rules** — and the evidence says the zone path was reachable without
  it while the account path was not. Add the group for the account scope; the live fact reads that list in its
  own `try` so a refusal there costs one line rather than the rest of the fact.
- **The answer carries two fields the schema does not list**: `paused` on the rule, and `name` on its `scope`.
  Both are modelled — `AccessRule.Paused` and `AccessRuleScope.Name` — and the name is the only thing an
  organization-scoped rule can be recognised by, an organization having no email address to show. Neither is
  sent in a request, because nothing published says Cloudflare accepts one there and a field it ignores would
  look like a rule that was switched off and was not.

⛔ Not wrapped: firewall **lockdowns** (`…/firewall/lockdowns`) and **user agent rules**
(`…/firewall/ua_rules`), both superseded by custom rules in the ruleset engine, and the WAF's own surfaces
(`…/firewall/waf/…`), which are plan-gated.

---

## Page Rules

`IPageRulesService` — `client.PageRules`

A page rule is a URL pattern in one zone and what Cloudflare should do differently for the requests that match
it: forward them elsewhere, cache them longer, switch a feature off. Rulesets are the newer engine and cover
most of the same ground, but a page rule still has actions no ruleset does — forwarding a URL among them — and
every zone carries a plan-sized allowance of them, **three on a free plan**.

**Cloudflare refuses this whole endpoint for an account-owned token.** A live run answered `400` with error
`1011`, *"Page Rules endpoint does not support account owned tokens"*, to the `GET` of the list as much as to
the `POST` — so a `cfat_` token, the kind an account creates for itself, cannot reach page rules at all, and no
permission group changes that. A **user-owned token** (`cfut_`, created under a person's profile) or the legacy
global API key is what does. The live fact therefore opts out with that reason for an account-owned token, and
the reach matrix reports the `1011` as the token kind rather than as a missing permission.

Three things about the shape are worth knowing. The path is `pagerules`, one word, not the `page_rules` the
resource's own name suggests. Only one page rule runs per request, and when two could match the one with the
**higher** priority wins — the opposite of an evaluation order counted from one. And Cloudflare publishes that
a rule's actions "can redirect to another URL or override settings, but not both", so `forwarding_url` has to
be the only action when it is there, which `EnsureValid()` checks locally.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(PageRuleListParameters?, zoneId)` | `GET /zones/{zone_id}/pagerules` | a **bare array** with no paging, because an allowance of three needs none. Filters: `status`, `order` ∈ `status`/`priority`, `direction`, `match` ∈ `all`/`any` (the same two words the DNS and zone lists use) |
| `GetAsync(pageRuleId, zoneId)` | `GET /zones/{zone_id}/pagerules/{pagerule_id}` | the same shape the list answers, so one type covers both; null when the zone has no rule of that id |
| `CreateAsync(PageRuleRequest, zoneId)` | `POST /zones/{zone_id}/pagerules` | the body **is** the rule: `targets` and `actions` required, `priority` defaults to 1, `status` to active. Needs **Page Rules Write** — or **Zone Write**, which Cloudflare accepts for all six calls |
| `UpdateAsync(pageRuleId, PageRuleRequest, zoneId)` | `PUT /zones/{zone_id}/pagerules/{pagerule_id}` | **replaces the whole rule**, which Cloudflare says will then exactly match what was sent: a target or an action left out is gone rather than kept |
| `EditAsync(pageRuleId, PageRuleRequest, zoneId)` | `PATCH /zones/{zone_id}/pagerules/{pagerule_id}` | changes only the fields it is given — how a rule is disabled or reordered without resending the rest. A patch that sets nothing is refused locally rather than sent |
| `DeleteAsync(pageRuleId, zoneId)` | `DELETE /zones/{zone_id}/pagerules/{pagerule_id}` | Cloudflare answers the id it deleted; deleting also frees the allowance slot |

- **An action's value is raw JSON**, and deliberately so. Page rules publish around thirty actions that share
  no value shape: `disable_apps` takes none, `cache_level` a word, `edge_cache_ttl` a number of seconds,
  `cache_key_fields` a nested object, and `cache_ttl_by_status` a map whose values are numbers *or* the words
  `no-cache` and `no-store`. `PageRuleAction` keeps `Value` as the JSON that arrived and offers factories for
  the common shapes — `NoValue`, `On`, `Off`, `Flag`, `Text`, `Number`, `Forwarding` — with `ValueAsText()` and
  `ValueAsNumber()` reading either spelling back, because Cloudflare is not consistent about which one an
  action uses.
- **Some action ids belong to page rules and some are zone settings.** `PageRuleActionIds` carries the
  seventeen of its own (`forwarding_url`, `cache_key_fields`, `edge_cache_ttl`, `disable_zaraz` and the rest);
  the others are zone-setting ids applied to one set of URLs, and are the words `ZoneSettingIds` already
  carries, so a page rule action and the setting it overrides are named the same thing.
- **A target is `{target: "url", constraint: {operator, value}}`**, with `url` the only kind published — kept
  as a constant rather than an enum with one member — and `operator` ∈ `matches`/`contains`/`equals`/
  `not_equal`/`not_contain`, which is an enum because it is a closed vocabulary. `PageRuleTarget.Matching()`
  builds the ordinary wildcard case.
- **Both timestamps are kept twice**: `CreatedOn` and `ModifiedOn` are the strings Cloudflare sent, and
  `CreatedOnUtc` and `ModifiedOnUtc` read them through `CloudflareTimestamps`. The endpoint publishes
  `date-time` for both, and a live answer is what settles whether that promise holds; until then the words
  survive beside the instant rather than being parsed on the way in and lost if they are not ISO.

---

## Turnstile

`ITurnstileService` — `client.Turnstile`

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(TurnstileWidgetListParameters?)` | `GET /accounts/{account_id}/challenges/widgets` | `page`, `per_page`, `filter`, `order` ∈ `id`/`sitekey`/`name`/`created_on`/`modified_on`, `direction`. Items are `TurnstileWidgetSummary` — **no secret**, which is what Cloudflare answers for a list. `filter`'s grammar is not published, so it is passed through rather than built here |
| `ListAllAsync(TurnstileWidgetListParameters?)` | `GET /accounts/{account_id}/challenges/widgets` | `IAsyncEnumerable<TurnstileWidgetSummary>`, walks every page and stops on an empty one as well as on `result_info` |
| `GetAsync(sitekey)` | `GET /accounts/{account_id}/challenges/widgets/{sitekey}` | `TurnstileWidget`, **secret included**; null on 404 |
| `CreateAsync(TurnstileWidgetRequest)` | `POST /accounts/{account_id}/challenges/widgets` | the body is the widget itself: `name`, `mode`, `domains` required, plus `region`, `clearance_level`, `bot_fight_mode`, `ephemeral_id`, `offlabel`. Answers the widget **with the secret Cloudflare generated**, so this is the moment to write it down |
| `UpdateAsync(sitekey, TurnstileWidgetRequest)` | `PUT /accounts/{account_id}/challenges/widgets/{sitekey}` | a `PUT`, so it replaces the whole widget: the three required fields go again even when only one is changing. There is no `PATCH` here |
| `RotateSecretAsync(sitekey, bool? invalidateImmediately)` | `POST /accounts/{account_id}/challenges/widgets/{sitekey}/rotate_secret` | body `{"invalidate_immediately":bool}`; left null, `{}` goes out and Cloudflare's grace period stays, which is what lets a deployment be rolled without a gap in verification. Answers the widget with its **new** secret under the **same** site key |
| `DeleteAsync(sitekey)` | `DELETE /accounts/{account_id}/challenges/widgets/{sitekey}` | tolerates 404 and answers null; Cloudflare echoes the deleted widget when there was one |

Three things here are not what the product's name suggests:

- The path is **`challenges/widgets`**, not `widgets` — Cloudflare's own generated client titles it
  `post /accounts/{account_id}/challenges/widgets`, and `GET …/widgets` is something else entirely.
- **A list carries no secret** where a single widget's answer does, and Cloudflare types the two apart for
  exactly that reason (`WidgetListResponse` beside `Widget`). So does this library: `TurnstileWidgetSummary`
  for a list item and `TurnstileWidget : TurnstileWidgetSummary` for one widget, which means a caller holding
  a list cannot read a secret out of it by accident and never has to wonder why one is null. Nothing here logs
  a secret either — `ToString()` is name and site key — and the live run reports a field it does not model by
  *name* rather than by value for the same reason.
- `deployed_via` and `last_modified_via` answer `unknown` as a **value of their own**, which is not the same
  thing as a value this library has not heard of. `TurnstileDeploymentSource` carries both: `NotRecorded` for
  Cloudflare's `unknown`, `Unknown` for the sentinel every enum here has.

Cloudflare validates every hostname in `domains` and refuses a pattern it does not accept: a live `PUT`
carrying `*.example.org` answered **400 `invalid value in \`domains\`, found invalid hostname`**. Nothing here
validates a hostname itself — Cloudflare is the authority on which ones it accepts, and it says so at once.

`offlabel` is one word on the wire and one word here, not `off_label`. Both timestamps are kept as the strings
that arrived, with `CreatedOnUtc` and `ModifiedOnUtc` beside them, because Workers AI proved that one product
area can answer a timestamp in a shape no example shows — a change there is then a one-line fix rather than a
broken model. `mode` (`non-interactive`, `invisible`, `managed`), `region` (`world`, `china`) and
`clearance_level` (`no_clearance`, `jschallenge`, `managed`, `interactive`) are enums that read an unrecognised
value as `Unknown` and refuse to write one.

**Verified live**: create with every field the endpoint takes, read the widget back with its secret, rotate to
a **different** secret under the **same** site key, find the widget in a list, and delete it. The list answered
that widget with no `secret` and with no field this library does not model — so the two-type split is a
confirmed answer rather than an assumption, and a single widget's answer carries nothing unpublished either.
A refused `PUT` is reported by the run rather than failed, because the offline suite pins that call's method,
path and exact body and a live run only adds what Cloudflare accepts.

⛔ Two of the widget's flags are **entitlement-gated**, and Cloudflare refuses each in the same words: a live
`PUT` with `offlabel: true` — serving the challenge without Cloudflare's branding — answered 400 with
`not entitled to create widgets with `offlabel` set to true`, and one with `ephemeral_id: true` — one-time
token ids — answered `not entitled to create widgets with `ephemeral_id` set to true`. Both are accepted as
`false` on a free account, which is how the live run creates a widget, so both fields stay wrapped and the run
changes a name and a mode instead, in line with paid features being out of scope. `bot_fight_mode` and
`clearance_level` have not been probed for the same gate.

⛔ Nothing else is left out of this resource: those six methods are all of `challenges/widgets`. Verifying a
token (`siteverify`) is not part of this API at all — it lives on `challenges.cloudflare.com`, and belongs to
the server that checked a visitor rather than to the account that owns the widget.

---

## Cloudflare IP ranges

`IIpRangesService` — `client.IpRanges`

One endpoint at the API root — `GET /ips`, with no account and no zone in the path, and **no permission group**:
Cloudflare publishes none for it, so any authenticated caller can read it, including a token that can do
nothing else. That is also what makes it the cheapest first call a client can make when it wants to know its
credentials are being sent the way Cloudflare expects, before spending one on something that could be refused.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListAsync(IpRangesListParameters?)` | `GET /ips` | `{etag, ipv4_cidrs[], ipv6_cidrs[]}`, plus `jdcloud_cidrs[]` when the call asks for `?networks=jdcloud` — the China network's ranges, which Cloudflare operates jointly with JD Cloud and lists separately from its own |

`etag` is a digest of the data rather than an HTTP header echoed back, and it is what makes caching these ranges
safe: Cloudflare changes them rarely and announces a change well ahead of time. `AllCidrs` puts the three lists
into one sequence — IPv4, then IPv6, then JD Cloud — because that is what a firewall rule wants handed to it.

`Contains(address)` is the reason to wrap this endpoint at all instead of pasting a list into a config file: it
answers whether an address sits inside one of the published ranges, which is the question an origin allowlist
and a `CF-Connecting-IP` check both come down to. An address is only ever compared with ranges of its own
family, and `IPAddress.Parse` is what decides the family — so an IPv4-mapped IPv6 address belongs to the IPv6
one and has to be mapped back before it can match an IPv4 range. Anything that does not parse, and any malformed
range in an answer, is passed over rather than thrown at: this is a filter, and the address usually came from
somewhere the caller does not control.

Older revisions of this endpoint also answered the China network's own ranges beside these, and the published
schema has moved more than once, so anything this version does not model arrives in `AdditionalData` and the
live fact reports which fields those were.

---

## Workers static assets

`IWorkersAssetsService` — `client.WorkersAssets`

The files a Worker serves as static assets. A deployment that carries them is three calls, and this service is
the middle two — the third is the script or version upload this library already wraps, whose metadata has carried
an `assets` block with a `jwt` field since before the endpoints that produce that JWT were wrapped here.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `CreateUploadSessionAsync(scriptName, WorkersAssetManifest, accountId)` | `POST /accounts/{account_id}/workers/scripts/{script_name}/assets-upload-session` | body `{"manifest":{"<path>":{"hash","size"}}}`; answers `{buckets[][], jwt}` — the hashes Cloudflare is **missing**, grouped into upload requests, plus the JWT that authorises sending them. The script has to exist first |
| `UploadAssetAsync(hash, bytes, jwt, contentType, accountId)` | `POST /accounts/{account_id}/workers/assets/upload/{hash}` | the raw bytes as the body, authorized **by the session JWT**. Not in the published reference: it is the single-asset call wrangler makes when the token allows it |
| `UploadAssetsAsync(items, jwt, accountId)` | `POST /accounts/{account_id}/workers/assets/upload?base64=true` | one `multipart/form-data` call for a bucket of assets: every part is named and file-named by its hash and holds the bytes as **base64 text** |

Both upload calls answer `{"jwt": …}`, and the published schema calls that "a completion JWT which can be
redeemed when creating a Worker version" — but neither is guaranteed to. A live run's single-asset upload
answered `200` with no token at all while the multipart call beside it answered one, so the rule is the one
Cloudflare's own client follows: keep the last non-empty token, and fall back to the session's own JWT, which
is already the right answer when the session asks for nothing. That token goes on the deployment that follows
as `UploadWorkerScriptRequest.Assets.Jwt` — see `client.Workers.UploadAsync` and
`client.Workers.CreateVersionAsync`.

The deployment's answer is how you check it took: `WorkerScript.HasAssets` says whether the Worker contains
static assets, beside `EntryPoint`, `HasModules`, `MigrationTag`, `StartupTimeMs`, `Tag` and `Tags`, all of
which a live upload answer carried and none of which this library modelled until it did. The `exports` map —
Cloudflare's declarative exports, a union of six shapes covering Durable Object classes and named entrypoints —
and `cache_options` stay in `AdditionalData`.

**These two endpoints take no API credentials at all.** Their only authorization is the session JWT, which is why
`CloudflareRequest.WithBearerToken(token)` exists: it puts the JWT in `Authorization` *and* tells the transport to
leave the configured API token or API key off the call. Setting the header with `WithHeader` is not enough — the
transport adds its own credentials first, and a repeated header is dropped rather than replacing what is already
there. A client configured with no credentials whatsoever can still upload assets it holds a JWT for.

**The manifest hash is the caller's to compute, and getting it wrong is silent.** Cloudflare publishes it as an
opaque `string` and never says how to build it; wrangler uses the **BLAKE3** hash of the file's bytes encoded as
base64 text with its extension appended without the dot, hex encoded and cut to the first 32 characters — the same
key scheme the Pages Direct Upload flow uses, and for the same reason it is not SHA-256 of the bytes. This library
does not implement BLAKE3 and does not guess at it: it takes the hash as given and uses that one string in all
three places that have to agree — the manifest entry, the upload path, and the multipart part name. Nothing
validates a wrong key. The upload answers 200, the deployment answers 200, and the asset 404s forever, which is
exactly the failure mode the Pages section below refuses to wrap its way into. Hash the way wrangler does, or let
wrangler do the uploading.

Manifest keys are root-relative request paths, not file system paths: the published schema's own example is
`/blog/hello-world.html`, and wrangler builds every key as a slash plus the file's path relative to the assets
directory with backslashes turned into slashes. `WorkersAssetManifest.NormalizePath` applies that rule, so
`blog\hello-world.html` becomes `/blog/hello-world.html` on every platform.

`buckets` is documented as "the requests to make to upload assets", and its shape carries two ideas worth keeping.
An inner list is one upload request, so Cloudflare decides how the work is grouped; and a hash that is absent from
every bucket is one Cloudflare already holds, so an unchanged site uploads nothing at all and the session's own
JWT is already the completion token. `WorkersAssetsUploadSession.NothingToUpload` says so, and
`RequestedHashes` flattens the buckets in order.

The session JWT also carries advice for the uploader, which `WorkersAssetsToken.TryRead` reads without verifying
the signature — there is no public key to verify against, and the token only ever goes back to Cloudflare.
`wrangler_single_asset_uploads` says whether the single-asset endpoint may be used, `edge_kv_upload_concurrency`
says how many buckets may be in flight (wrangler's fallback is 50, mirrored as
`WorkersAssetsToken.DefaultUploadConcurrency`, and only a positive number is honoured), and `exp` says when the
token stops working — a deployment that waits too long has to ask for a new session rather than retry the upload.

An asset whose media type is unknown goes up as `application/null`, wrangler's sentinel that Cloudflare's asset
API parses as "store no content type", so the asset is served without one rather than with a wrong one. That is
`WorkersAssetContentTypes.Unknown` and the default of both upload calls: sending
`application/octet-stream` instead would make a browser download an image it should display.

Cloudflare has answered the session endpoint with a null result before (cloudflare/workers-sdk issue 9465), so
`CreateUploadSessionAsync` goes through `EnsureResult()` and turns that into a `CloudflareApiException` naming the
request and carrying the ray id, rather than a null reference two calls later.

**Permission:** the session endpoint wants `Workers Scripts` Write
(`e086da7e2179491d91ee5f35b3ca210a`) — the same group as the script upload it belongs to. The two upload
endpoints want no group at all, only the JWT.

⛔ **Not wrapped:** Workers for Platforms has its own copy of the session endpoint under
`/accounts/{account_id}/workers/dispatch/namespaces/{namespace}/scripts/{script}/assets-upload-session`. This
library wraps no dispatch-namespace endpoint, here or anywhere else. Nothing else is left out of this resource:
`assets-upload-session` and the two upload paths are all of it, and there is no read, list or delete call for
assets — Cloudflare garbage-collects what no deployment references.

---

## Durable Objects

`IDurableObjectsService` — `client.DurableObjects`

Two read-only endpoints: the namespaces an account owns, and the objects inside one namespace. Neither has a
write, because neither resource is made through the API — a namespace comes into being when a Worker script
that exports a Durable Object class is deployed (through the deployment's `durable_object_classes` binding,
with a migration when storage has to move with it), and an object when a script asks for one by name or by id.
Cloudflare lists **Workers Scripts Read** first among the permissions these endpoints accept, and a live run
confirmed it: the token this suite runs with lists namespaces with no group of its own, and an account with no
Durable Object class deployed answers `200` with an empty list rather than refusing.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListNamespacesAsync(DurableObjectNamespaceListParameters?, accountId)` | `GET /accounts/{account_id}/workers/durable_objects/namespaces` | `{id, class, name, script, use_sqlite}` per namespace; page-number pagination, `per_page` up to 1000 |
| `ListAllNamespacesAsync(accountId, pageSize)` | the same, walked | stops when Cloudflare reports no further page **or** answers an empty one |
| `ListObjectsAsync(namespaceId, DurableObjectListParameters?, accountId)` | `GET /accounts/{account_id}/workers/durable_objects/namespaces/{namespace_id}/objects` | `{id, hasStoredData}` per object; **cursor** pagination, sized by `limit` |
| `ListAllObjectsAsync(namespaceId, pageSize, accountId)` | the same, walked | follows `result_info.cursors.after`, and stops when Cloudflare hands back the cursor it was given |

The two endpoints disagree about pagination, and that disagreement is the trap worth naming: namespaces take
`page` and `per_page`, objects take a cursor and `limit`. A caller who assumed one style for both would ask
for nothing and get an answer that looked empty. `DurableObjectNamespaceListParameters` exists only because
the shared `CloudflarePagedParameters` is abstract; `DurableObjectListParameters` carries the cursor and the
limit, and its `WithCursor(cursor)` is what the walk uses to move on without losing the page size.

`hasStoredData` is camelCase in Cloudflare's own reference — the only field this library wraps that is. The
property is named for the wire rather than corrected, because a caller comparing what arrives against what the
reference publishes has to see the same spelling. An object Cloudflare says nothing about reads as
`HasStoredData == null`, which is not the same as one it reports as holding no data.

Listing objects is as far as the REST v4 surface goes: reading an object's storage means calling the object's
own HTTP entry point, or talking SQLite to it over a WebSocket, and neither is part of the API this library
wraps. `use_sqlite` on the namespace is what tells a caller which of the two an object would want.

---

## URL normalization

`IUrlNormalizationService` — `client.UrlNormalization`

One zone-level setting and three verbs: read it, set it, or delete it to fall back to Cloudflare's default.
Which normalizer a zone runs, and on which of its requests, is what decides how a URL looks to the rules that
match against it.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `GetAsync(zoneId)` | `GET /zones/{zone_id}/url_normalization` | `{scope, type}` — and **`null` is a published answer**, so a read can come back with no setting at all |
| `UpdateAsync(UrlNormalizationSettings, zoneId)` | `PUT /zones/{zone_id}/url_normalization` | Cloudflare requires both fields, so an update never changes one of them on its own |
| `ResetAsync(zoneId)` | `DELETE /zones/{zone_id}/url_normalization` | resets to the default, and answers with the setting it fell back to |

`scope` is `incoming`, `both` or `none`; `type` is `cloudflare` or `rfc3986`. Both are modelled as enums with
an `Unknown = 0` member, so a value Cloudflare adds later reads as `Unknown` instead of throwing — and
`EnsureValid()` refuses to send one back, because a `PUT` carrying a value this library cannot spell would be
a `PUT` of nothing. That is also why a setting read from the wire can be handed straight back to
`UpdateAsync`: when it can, the two fields arrive; when they do not, the exception says which one is missing
rather than Cloudflare saying it a round trip later.

**No permission group is named for this endpoint, and the one Cloudflare's reference lists cannot be trusted.**
A live run was refused `403 [10000]` by a token that reads `GET /accounts/{account_id}/rulesets` in the same
run — so it holds **Account Rulesets Read**, which the reference lists among the two dozen rules-family groups
this endpoint accepts, "at least one required". One of two things is true and they cannot be told apart from
outside: the list is over-broad, or a zone-scoped endpoint wants a zone-scoped grant the account-scoped group
does not carry. Guessing a group would send a reader to add a permission that may grant nothing, so this
endpoint is wrapped and offline-tested, the live fact reports the refusal it saw and skips, and the reach
matrix does not probe it.

---

## Origin post-quantum encryption

`IOriginPostQuantumEncryptionService` — `client.OriginPostQuantumEncryption`

Two verbs over one zone-level setting, and two things about it a caller has to know before using either.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `GetAsync(zoneId)` | `GET /zones/{zone_id}/cache/origin_post_quantum_encryption` | `{id: "origin_pqe", editable, value, modified_on}` — every field but `modified_on` is required, so a null result is a broken answer rather than an empty one |
| `UpdateAsync(OriginPostQuantumEncryptionRequest, zoneId)` | `PUT` the same path | the body is the one field `value`: `off`, `supported` or `preferred` |

**The path is not where the subject lives.** The setting is about the TLS handshake with the origin, and this
library keeps the model with the other SSL resources, but Cloudflare publishes the endpoint under the zone's
`cache` segment. The path here says `cache` because that is where the endpoint is; nothing about caching is
involved.

**Writing to it no longer changes the zone.** Cloudflare replaced this control with automatic key exchange —
which this library already wraps as `client.Ssl.GetAutoOriginTlsKexAsync` and
`client.Ssl.SetAutoOriginTlsKexAsync(enabled)`, on the zone setting `auto_origin_tls_kex` — and documents that
requests to this endpoint "are no-ops and do not change a zone's post-quantum key agreement". The endpoint
still answers, so it is still wrapped, but the answer has to be read as what the endpoint returned rather
than as what the zone now does. The live fact writes to it once and reads it back, because that is the only
evidence available: when the value did not move, the fact says so in a `Warn` that names the documentation
predicting it, instead of failing on a call that behaved exactly as Cloudflare says it would.

Cloudflare publishes no permission group named for this setting either, but here the assumption held: a live
run read it with the zone's **SSL and Certificates** group (`origin_pqe=200`, three fields) and the write fact
changed the value and read the change back. That last part is worth repeating, because it is the opposite of
what the documentation predicts — Cloudflare says requests to this endpoint are no-ops, and on the zone this
suite ran against the value moved. The fact reports either outcome: a value that did not move produces a
`Warn` naming the documentation that predicted it, and a value that moved simply lands in the log.

---

## Radar reference data

`IRadarService` — `client.Radar`

The reference data the rest of Cloudflare Radar is queried by: locations, autonomous systems and what
Cloudflare knows about how they relate to one another, the geolocation tree, the entity behind an IP address,
the global search across all of them, the annotations Cloudflare attaches to its own data, and the datasets it
publishes for download. Radar has 269 published endpoints; these are the ones that answer *reference data*
rather than measurements, and they are what every other one needs first, because a timeseries query is made
*by* location code and ASN. The rankings sit in the next section; the rest of radar — the timeseries, summary
and top-N endpoints for HTTP, DNS, BGP, bots, attacks, email, quality and the AI catalogue — is still to come.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `ListLocationsAsync(RadarLocationListParameters?)` | `GET /radar/entities/locations` | `{locations: [{alpha2, continent, latitude, longitude, name, region, subregion}]}`; filters by `continent`, `region`, `subregion`, `location` |
| `ListAllLocationsAsync(pageSize)` | the same, walked | moves along by `offset`; stops on a page whose length is not the one asked for |
| `GetLocationAsync(location)` | `GET /radar/entities/locations/{location}` | adds `confidenceLevel`, which the list does not answer |
| `ListAsnsAsync(RadarAsnListParameters?)` | `GET /radar/entities/asns` | `{asns: [{asn, country, countryName, estimatedUsers, name, aka, orgName, website}]}`; filters by `asn` (comma-separated), `location`, and orders by `orderBy=ASN`\|`POPULATION` |
| `ListAllAsnsAsync(pageSize)` | the same, walked | a long list — tens of thousands of ASNs — so it takes a page size and a cancellation token |
| `GetAsnAsync(asn)` | `GET /radar/entities/asns/{asn}` | adds `confidenceLevel`, `source`, `related[]` and the per-location breakdown of `estimatedUsers` |
| `GetAsnForIpAsync(ip)` | `GET /radar/entities/asns/ip?ip=` | the same detail, for the ASN behind an address |
| `GetEntityForIpAsync(ip)` | `GET /radar/entities/ip?ip=` | `{ip: {asn, asnLocation, asnName, asnOrgName, ip, ipVersion, location, locationName}}` — **every field a string**, the ASN included |
| `ListGeolocationsAsync(RadarGeolocationListParameters?)` | `GET /radar/geolocations` | `{geolocations: [{code, geoId, latitude, locale, longitude, name, parent, type}]}`, with `parent` the same shape again |
| `GetGeolocationAsync(geoId)` | `GET /radar/geolocations/{geo_id}` | one place, with the tree above it; the id is the answer's `geoId`, **not** its `code` |
| `SearchAsync(RadarSearchParameters)` | `GET /radar/search/global` | `{search: [{code, name, type}]}`; `include` and `exclude` are repeated parameters over twelve categories |
| `SearchAsync(query, limit)` | the same | the convenience overload for a plain search |
| `ListAnnotationsAsync(RadarAnnotationListParameters?)` | `GET /radar/annotations` | `{annotations: [{id, asns, asnsDetails, dataSource, description, endDate, entities, eventType, geoIds, linkedUrl, locations, locationsDetails, origins, originsDetails, outage, scope, startDate, tags}]}`; nineteen filters, and the four whose values Cloudflare publishes as a closed list are enums |
| `ListOutagesAsync(RadarAnnotationListParameters?)` | `GET /radar/annotations/outages` | the same shape and the same parameters, narrowed to outages |
| `ListOutageLocationsAsync(RadarOutageLocationsParameters?)` | `GET /radar/annotations/outages/locations` | `{annotations: [{clientCountryAlpha2, clientCountryName, value}]}` — a different shape under the same property name, with no `offset` to walk by |
| `ListDatasetsAsync(RadarDatasetListParameters?)` | `GET /radar/datasets` | `{datasets: [{id, alias, description, meta, tags, title, type}]}`; `date` is published with `format: date`, so it is sent without a time part |
| `RequestDatasetDownloadAsync(datasetId, format)` | `POST /radar/datasets/download` | `{dataset: {url}}` — the id travels in the body, the format in the query string, and the answer is an address to fetch rather than the file: an R2 url signed for an hour. With `format=CSV` Cloudflare does not answer that envelope at all — it answers one line of text, `Dataset url https://…` — so the method reads the address out of either and returns it whole |
| `GetAsnAsSetAsync(asn)` | `GET /radar/entities/asns/{asn}/as_set` | `{as_sets: [{as_members_count, as_set_members_count, as_set_upstreams_count, asn_cone_size, irr_sources, name, hierarchical_asn, inferred_asn, peeringdb_asn, descr, as_members, as_set_members, mnt_by, admin_c, tech_c, org, inference_matches, is_cf_transit}], paths: [[…]]}` — the last nine are answered but unpublished |
| `GetAsnRelationshipsAsync(asn, asn2?)` | `GET /radar/entities/asns/{asn}/rel` | `{meta: {data_time, query_time, total_peers}, rels: [{asn1, asn1_country, asn1_name, asn2, asn2_country, asn2_name, rel, aspa_validated}]}`; `rel` arrives as `peer` and `provider-customer`, and `aspa_validated` is unpublished |
| `ListBotnetThreatFeedAsync(RadarBotnetThreatFeedParameters?)` | `GET /radar/entities/asns/botnet_threat_feed` | `{ases: [{asn, country, name, rank, rankChange}], meta: {date, total, compareDate}}`; `asn` takes `-174` to leave AS174 out |

Four things about these endpoints are unlike the rest of the API this library wraps, and each one is the
reason a caller would otherwise get nothing back and call it a bug:

**The result is wrapped in a property named for it.** `result` is `{locations: […]}`, `{asn: {…}}`,
`{search: […]}` — never the array or object itself. Each call here unwraps one level, following the same
`…ListResult` idiom R2's `{buckets: […]}` uses, so a caller gets `CloudflarePage<RadarLocation>` rather than a
wrapper to dig through.

**The query parameters are camelCase.** `orderBy`, `limitPerGroup` — not `order_by` and `limit_per_group`.
The rest of the v4 API is snake_case on the wire, and a caller who assumed that here would send a parameter
Cloudflare ignores.

**The lists page with `offset` and `limit`, and answer no `result_info`.** There is no total to read and no
cursor to follow, so the only signal that a walk has ended is the page's own length: `ListAllLocationsAsync`
and `ListAllAsnsAsync` stop when a page comes back with any number of items other than the limit they asked
for — fewer when the list ran out, more when Cloudflare ignored the limit and sent everything at once, which
would otherwise be walked into a second time.

**The geolocation tree carries an id its own schema does not publish.** Cloudflare's reference lists `code` and
`locale` on a geolocation and nothing else beyond latitude, longitude, name, parent and type — yet every place
in a live answer, and every one of its parents, also carries `geoId` (`3175395` for Italy, `6269131` for
England). `geoId` matters because `GET /radar/geolocations/{geo_id}` reads a single place **by it**: asking by
`code` is answered `400 [2001]`, a refusal that looks like a bad credential or a wrong path and is neither. It
is modelled rather than left to `AdditionalData`, because the one field a caller needs to make the next call is
exactly the field nobody would think to look for there.

**The fields are camelCase too, and inconsistently.** One answer carries `countryName`, `estimatedUsers` and
`confidenceLevel` beside `as_members_count` in a neighbouring one, and `GET /radar/entities/ip` answers the
ASN as a *string* where its neighbour answers it as a number. Every property is pinned to the wire spelling
with `[JsonPropertyName]`, `RadarIpEntity.AsnNumber` parses the string when it is one, and a four-byte ASN is a
`long` all the way down because the private range starts past `int.MaxValue`.

**The second half of radar answers two spellings at once, and one list with holes in it.** An AS-SET is
published in snake_case — `as_members_count`, `irr_sources`, `asn_cone_size` — while the annotations beside it
are camelCase, and the botnet ranking mixes both inside a single item (`rank` beside `rankChange`). An
annotation carries the same ASN twice, once as a number in `asns` and once as a string in `asnsDetails[].asn`,
so `RadarAnnotationAsn` parses the string into `AsnNumber` rather than making a caller choose which of the two
to trust. And `locationsDetails` is published as a list of *nullable* items: a location Cloudflare could not
resolve keeps its slot as `null`, which is what keeps it aligned by position with the `locations` list beside
it.

**One call answers text where it publishes JSON.** `POST /radar/datasets/download` is documented as answering
`{dataset: {url}}`. A run asked it for `format=CSV` about three datasets the list had just described — 3033
`ranking_top_1000000`, 3032 `ranking_top_500000`, 3031 `ranking_top_200000` — and all three came back with
nothing a JSON parse could hold. Asked again through the transport for the body itself, the endpoint answered
`Dataset url https://…/bucket-1000000-20261102-20261109.csv?X-Amz-…`: one line of text, signed for an hour,
and not JSON, so `HasEnvelopeRoot` is false and the transport returns an empty result rather than throwing. A
call that had worked read as one that had answered no address. `RequestDatasetDownloadAsync` reads the address out
of either shape and returns it whole — the signature is what makes it usable — while the integration suite
reports only the scheme, host and path, because a run's annotations are public.

Reading it took two runs. The first matched the prefix `Dataset url ` exactly, and the next run still answered
nothing, which is what a separator a log cannot show looks like: a tab prints as a space and a byte-order mark
prints as nothing. So the method takes the first token in a text answer that parses as an absolute http(s)
address, and leaves a body that looks like JSON alone — inventing an address out of an envelope that offered
none would be worse than reporting that there was not one. The unit tests pin the spellings a log cannot tell
apart: tab, lower case, byte-order mark, trailing newline, and words around the address.

**Eleven fields arrive that Cloudflare does not publish, and they are the interesting ones.** The live run read
`GET /radar/entities/asns/{asn}/as_set` against AS13335 and was answered, beside every field the schema lists,
the IRR record behind each set — `descr`, `mnt_by` with its maintainers' handles, sources, contacts and
timestamps, `admin_c`, `tech_c`, `org` — plus `as_members`, `as_set_members`, `inference_matches` (how
Cloudflare worked out which ASN the set belongs to: `{"asn":134654,"method":"peeringdb","detail":null}`) and
`is_cf_transit`. `GET /radar/entities/asns/{asn}/rel` answers an `aspa_validated` boolean beside each
relationship — whether an RPKI ASPA object authorises it — and spells `rel` as `peer` and `provider-customer`
rather than the upstream/downstream its name suggests. `GET /radar/datasets` answers an `alias` beside its `id`,
such as `ranking_top_200000`, which
is the name that identifies a dataset across dates and the one `GET /radar/datasets/{alias}` would take. All
ten are modelled, because a field a caller has to fish out of `AdditionalData` is a field they will not know to
look for — the same call the geolocation `geoId` forced.

They are typed by what was seen, not by what the name suggests. `mnt_by`, `admin_c`, `tech_c`, `descr` and
`inference_matches` arrived filled in, so they are typed as they arrived. `as_members`, `as_set_members` and
`org` arrived empty or null in every answer, so they are read as the JSON they arrive as: a guessed item type
that turns out wrong does not cost one field, it fails the whole read.

`is_cf_transit` is the case that proved the rule. Its name was in an answer and its value was not, so it was
typed as a `bool` — and from that run on, `GET /radar/entities/asns/{asn}/as_set` could not be read at all:
the HTTP call answered 200 with `success: true` and the whole response failed to parse, every AS-SET in it
lost. It is now read as the JSON that arrives (`IsCloudflareTransitRaw`) with a `bool?` beside it
(`IsCloudflareTransit`) that reads `true`/`false`, `1`/`0` and `"true"`/`"false"`, and answers `null` for
anything else. An integration run reports a refusal with the exception message and the body Cloudflare sent,
because a parse failure prints as `OK []` and reads like a defect in the report otherwise. `aspa_validated`,
found by the same run once the AS-SET read worked again, is the other side of the rule: it arrived as both
`true` and `false`, so it is a `bool?`.

**Two of these page but are not returned as pages.** `GetAsnRelationshipsAsync` and
`ListBotnetThreatFeedAsync` answer a `meta` object beside their list, and the botnet feed's `meta.total` is the
only total any radar endpoint offers — radar answers no `result_info`, so there is nothing else to page by.
Wrapping the list alone in a `CloudflarePage<T>` would throw that away, so both are handed back as the objects
Cloudflare sends.

⛔ **Not wrapped:** the `format` parameter on the read endpoints, which can ask for `CSV`. This library's
transport reads the JSON envelope, and a CSV body would come back as an empty result rather than as an error —
silence is worse than not offering the choice. `GET /radar/datasets/{alias}` is left out for the same reason and
a stronger one: it has no JSON answer at all, and Cloudflare's own SDK asks it with `Accept: text/csv` and reads
a string. `POST /radar/datasets/download` is where a bulk CSV belongs and *is* wrapped, because its answer stays
the JSON envelope and the format only names what the file behind the returned URL holds — which is why
`RadarDatasetFormat` is the one place in radar where a `format` is typed. The `alias` that endpoint would take
is still modelled on `RadarDataset`, because it identifies a dataset across dates the way its
numeric id does not.

**Radar's reference data needs no permission at all — and Cloudflare's reference names the wrong one.** The
published permission list for these endpoints asks for **User Details Read**, a group that appears nowhere in
Cloudflare's catalogue. The catalogue does carry **Radar Read**
(`dfe525ec7b07472c827d8d009178b2ac`), which the published list does not mention. Live runs read all nine of
these endpoints — and the six rankings in the next section, whose published list names the same nonexistent
group — with a token holding neither, `200` on each, so radar's reference data and rankings are public in
practice and this is the same overstatement the reference makes for URL normalization.

The reach matrix keeps **Radar - Read** as these rows' group anyway, with the id from the live catalogue, so
that a refusal in future names a group that exists and a token URL can carry. `Radar Read` is also in
`WANTED_GROUPS` ahead of need, because the timeseries and summary endpoints that query *by* this reference
data are the next work and are the ones that will ask for it. Only a live read of
`GET /accounts/{account_id}/tokens/permission_groups` finds that id — it is in no published list, which is why
every integration run prints the groups its catalogue has that the ones already wrapped do not.

---

## Radar ranking

`IRadarService` — `client.Radar`

The rankings behind Radar's own top pages: which domains the Internet used, which services were behind that
traffic, where one domain sits in the list, and how each of those moved over time. Six endpoints, and they
have more in common with each other than with the reference data beside them — every one of them answers a
*numbered series* rather than a list.

| Method | Endpoint | Notes |
| --- | --- | --- |
| `GetTopDomainsAsync(RadarRankingTopParameters?)` | `GET /radar/ranking/top` | `{meta: {confidenceInfo, dateRange, lastUpdated, normalization, top_0: {date}, units}, top_0: [{domain, rank, categories: [{id, name, superCategoryId}], pctRankChange?}]}`; filters `date`, `domainCategory`, `limit`, `location`, `name`, `rankingType` |
| `GetTopDomainTimeseriesGroupsAsync(RadarRankingTimeseriesGroupsParameters?)` | `GET /radar/ranking/timeseries_groups` | `{meta: {aggInterval, confidenceInfo, dateRange, lastUpdated, normalization, units}, serie_0: {timestamps: [...], ...}}`; periods as `dateStart`/`dateEnd` or as `dateRange`, plus `domains`, `domainCategory`, `location`, `name`, `rankingType` — and no `date`, which is why its parameters are not the top list's |
| `GetDomainRankingAsync(domain, RadarDomainRankingParameters?)` | `GET /radar/ranking/domain/{domain}` | `{details_0: {categories, bucket?, rank?, top_locations?}, meta: {dateRange}}`; `includeTopLocations=true` is what makes `top_locations` arrive, and every field but the categories is optional |
| `ListInternetServiceCategoriesAsync(RadarInternetServiceCategoriesParameters?)` | `GET /radar/ranking/internet_services/categories` | `{categories_0: [{name}]}` — the names the two service rankings are filtered by |
| `GetTopInternetServicesAsync(RadarInternetServiceTopParameters?)` | `GET /radar/ranking/internet_services/top` | `{meta: {..., top_0: {date, serviceCategory}, units}, top_0: [{rank, service}]}`; filters `date`, `limit`, `name`, `serviceCategory` |
| `GetInternetServiceTimeseriesGroupsAsync(RadarInternetServiceTimeseriesGroupsParameters?)` | `GET /radar/ranking/internet_services/timeseries_groups` | the same timeseries shape as the domain ranking, filtered by `serviceCategory` instead of `domainCategory` |

**Cloudflare numbers its series and publishes only the first.** Ask for two dates, or two `name` labels, and
the answer carries `top_0` and `top_1` — `name` is documented as "array of names used to label the series in
the response", which is what the numbering is for. Only the `_0` key is in the published schema, so only the
first series is typed: `RadarRankingTop.Top0`, `RadarTimeseriesGroups.Serie0`, `RadarDomainRanking.Details0`,
`RadarInternetServiceCategories.Categories0`. The rest arrive in each result's `AdditionalData`, and
`SeriesNames` lists them, because a numbered key nobody knows to look for is a series nobody reads. Typing
`top_1` from its neighbour's shape is exactly the guess that broke `GET /radar/entities/asns/{asn}/as_set` —
see the reference-data section — so the integration run reports whether a second series ever arrives and what
it holds, and the type follows the answer rather than preceding it.

**The series values are the caller's own names, so they cannot be typed at all.** A timeseries group answers
`timestamps` beside one array per series, and the array is named after the domain or service it measures —
`google.com`, `AWS`. Cloudflare publishes the group as `timestamps` plus an open set of keys, and one of those
arrays answers strings where its neighbour answers numbers, so they are read as the JSON they arrive as with
`SeriesNames` listing what came. A `List<double>` here would be a guess about somebody else's data.

**The meta is one shape across every radar timeseries endpoint.** `RadarSeriesMeta` — `aggInterval`,
`confidenceInfo`, `dateRange`, `lastUpdated`, `normalization`, `units` — is what all of them answer, and
`RadarTopListMeta` is the same without the aggregation interval and with the first series' label, so the
timeseries endpoints still to come (HTTP, DNS, BGP, bots, attacks) reuse both rather than adding a meta per
product. Three details in it are worth knowing:

- `aggInterval` and `normalization` are answers, so they stay the strings Cloudflare sends, with
  `RadarAggregationIntervals` and `RadarNormalizations` holding the published spellings to compare against. An
  enum would turn a value this library has not met into `Unknown` and drop what Cloudflare said.
- `confidenceInfo.level` is a `double`. Cloudflare publishes it as a number with no range, and one answer
  carried a fraction where another carried a whole number: an `int` would read `95` and fail on `0.85`, and a
  field that fails to read takes the whole meta object with it.
- `confidenceInfo.annotations` are the same objects `GET /radar/annotations` answers, with fewer of their
  fields filled in, so they are read as `RadarAnnotation` rather than as a second type that would have to be
  kept in step with the first. That is where `isInstantaneous` is published, and `RadarAnnotation` reads it
  now — nullable, because the annotation list does not answer it.

**Dates are three different things here.** `date` is a day, sent as `yyyy-MM-dd`; `dateStart` and `dateEnd` are
timestamps, sent as `yyyy-MM-ddTHH:mm:ssZ`; and `dateRange` is Cloudflare's own grammar — a count of days or
weeks, `7d` or `12w`, with `control` appended to ask for the period before it as well. The first two are typed
as `DateTimeOffset` and formatted on the way out. `dateRange` stays the string a caller writes: the grammar is
published in prose, and a range this library refused locally but Cloudflare accepted would be a limit of this
library, not of the API.

`rankingType` is the one enum in this section — `POPULAR`, `TRENDING_RISE`, `TRENDING_STEADY` — because it is a
filter rather than an answer, and a value this library cannot spell has nothing to send:
`RadarRankingParameters.EnsureValid` says so before the request is built. The same check refuses a `limit` that
asks for fewer than one entry.

**What the first live run answered.** All six read `200` with a token holding no radar permission at all —
the same overstatement the reference data's published permission list makes, which names a *User Details
Read* group that appears nowhere in Cloudflare's catalogue. The ranked domains, their categories and the
ranked services carried no field this library does not model; each answer held one series, so no `top_1` or
`serie_1` arrived to type; `meta.top_0.date` and its `serviceCategory` came where they are published;
`pctRankChange` arrived on at least one of the two rankings the run asked for, popular and `TRENDING_RISE`,
which is the pair the surprise was written to tell apart; and `confidenceInfo.level` read as the `double` it
is typed as. The reach-matrix probe for `GET /radar/entities/asns/{asn}/as_set` beside them keeps answering
`503` with a body that is not JSON while the library's own call to the same path reads `200` in the same run,
which is why a probe asks twice before the matrix believes it.

⛔ **Not wrapped:** `format=CSV` on any of these six, for the reason the reference-data reads have — Cloudflare
replaces the envelope with a CSV body, which surfaces as an empty result rather than as an error. The dataset
download is the one radar call that does take a `format`, because it answers an address to fetch rather than
the file itself.

---

## Radar bot traffic

| Endpoint | Method |
| --- | --- |
| `GET /radar/bots` | `ListBotsAsync`, `ListAllBotsAsync` |
| `GET /radar/bots/{bot_slug}` | `GetBotAsync` |
| `GET /radar/bots/summary/{dimension}` | `GetBotSummaryAsync` |
| `GET /radar/bots/timeseries` | `GetBotTimeseriesAsync` |
| `GET /radar/bots/timeseries_groups/{dimension}` | `GetBotTimeseriesGroupsAsync` |
| `GET /radar/bots/crawlers/summary/{dimension}` | `GetWebCrawlerSummaryAsync` |
| `GET /radar/bots/crawlers/timeseries_groups/{dimension}` | `GetWebCrawlerTimeseriesGroupsAsync` |
| `GET /radar/verified_bots/top/bots` | `GetTopVerifiedBotsAsync` |
| `GET /radar/verified_bots/top/categories` | `GetTopVerifiedBotCategoriesAsync` |

Nine paths, and between them the three shapes radar's measurement endpoints use. Nothing here is scoped to an
account or a zone either, so none of these calls needs one.

**A dimension in the path.** Four of the nine take the thing the answer is broken down by as a path segment
rather than as a filter: `BOT`, `BOT_KIND`, `BOT_OPERATOR` and `BOT_CATEGORY` for the bots, and `CLIENT_TYPE`,
`USER_AGENT`, `REFERER`, `CRAWL_REFER_RATIO`, `VERTICAL`, `INDUSTRY`, `RESPONSE_STATUS` and
`RESPONSE_STATUS_CATEGORY` for the crawlers. Both are enums
(`RadarBotDimension`, `RadarWebCrawlerDimension`), because a dimension that cannot be spelled has no path to
be put in, and the service says so before building the request.

**A summary is a dictionary.** `summary_0` holds one number per value of the dimension asked about, keyed by
that value — `{"BOT": "63.65", "AGENT": "1.20"}` for `BOT_KIND`. Cloudflare publishes it as a map of strings
and it is read as one (`RadarSummary.Summary0`), because naming those keys in a class would mean this library
inventing a field per bot kind, per operator and per category. The numbers stay strings: they are shares whose
precision Cloudflare decides.

**Named groups on the timeseries.** `GET /radar/bots/timeseries` answers no `serie_0` at all. Its groups are
named after whatever the call filtered by — ask about `googlebot` and `gptbot` and the result carries a
`googlebot` group and a `gptbot` group — so none of them is published and `RadarNamedTimeseries` reads them by
name: `GroupNames` lists what arrived and `Group(name)` returns it, with the values inside still the JSON they
came as. The grouped endpoint beside it (`timeseries_groups/{dimension}`) *does* answer the numbered
`serie_0`, and that is the same `RadarTimeseriesGroups` the rankings use, so one model serves both families.

**Two spellings of one idea.** An aggregation interval is asked for as `15m`, `1h`, `1d` or `1w`
(`RadarAggregationInterval`) and reported back as `FIFTEEN_MINUTES`, `ONE_HOUR`, `ONE_DAY` or `ONE_WEEK`, so
the filter is an enum and the answer is a string with `RadarAggregationIntervals` holding the spellings. The
same is true of normalization, in the other direction: Cloudflare *reports* eight of them
(`RadarNormalizations`) and *accepts* three on the crawler timeseries (`PERCENTAGE`, `MIN0_MAX`,
`PERCENTAGE_CHANGE`), which is why `RadarTimeseriesNormalization` is not the same list.

**Singular where the summary is plural.** `GET /radar/bots` filters by `botCategory`, `botOperator`,
`botVerificationStatus` and `kind` — one value each, and note `kind`, not `botKind` — while the summaries
beside it take lists of all four under their longer names. Both are Cloudflare's own spellings for the same
idea, so `RadarBotListParameters` and `RadarBotParameters` are separate types rather than one with optional
plurals. `RadarBot` is one class serving the list and the single read, because the reference publishes the
single read as the list entry plus `operatorUrl`, `userAgents` and `signatureAgentUrl`.

**Verified bots are the ranking shape.** Both `/radar/verified_bots/top/*` endpoints answer `top_0` as a list
of entries beside a meta object, with no `meta.top_0` label of the kind the domain rankings carry, so they are
read as `RadarTopList<RadarVerifiedBot>` and `RadarTopList<RadarVerifiedBotCategory>` over the shared
`RadarSummaryMeta`. Both take the same nine parameters, and a verified bot's share of traffic arrives as a
string and stays one.

**`limitPerGroup` has a minimum of two — and some dimensions refuse it altogether.** Two rules, both
`400 [2001]`, and neither of them published. A per-group limit of one is answered `Too small: expected number
to be >=2` by all four of these endpoints that take one, while `limit=1` on the rankings beside them reads
fine. That is a minimum Cloudflare enforces rather than one this library invented, so
`RadarQuery.CheckLimitPerGroup` refuses it in both the bot and the crawler trees and names the `400 [2001]` it
would have earned. The `limitPerGroup` on `GET /radar/search/global` keeps its older check for at least one
entry, because nothing has proved that endpoint's minimum is the same.

Give those endpoints a *two* and a second rule appears. `GET /radar/bots/summary/BOT_KIND` answers
`limit_per_group is incompatible with BotsGroupingDimensions.BOT_KIND dimension`, and the crawlers'
`CLIENT_TYPE` answers the same about `CrawlersGroupingDimensions.CLIENT_TYPE`, while `BOT_CATEGORY` and
`RESPONSE_STATUS_CATEGORY` take a per-group limit on the grouped endpoints beside them. The pattern looks like
a dimension whose values are a short closed list — two bot kinds, four client types — having no groups worth
limiting, but Cloudflare publishes no list of which dimensions accept the parameter, so this library does not
guess one: `limitPerGroup` stays optional, is sent only when a caller sets it, and the live fact asks every
dimension it can and reports the split on each run. The two summary probes in CI ask without it, because a
probe is about reach.

That map's first answer: `BOT`, `BOT_OPERATOR` and `BOT_CATEGORY` accepted a per-group limit of two, and so
did the crawlers' `USER_AGENT`; `BOT_KIND` and the crawlers' `CLIENT_TYPE` refused it. Two bot kinds and four
client types on the refusing side, and the open-ended value sets on the accepting one, which is the closed-list
pattern above - observed rather than assumed, and re-observed every run.

**What the first live run answered.** All nine read `200` with a token holding no radar permission — the same
overstatement the two radar sections before this one found, where the reference names a *User Details Read*
group that appears nowhere in Cloudflare's catalogue. One call in that run was refused, and it was this
library's own doing rather than Cloudflare's: it sent `limitPerGroup=5` with `BOT_KIND`, which is the
incompatibility described above, and the same read answers once the parameter is left out. `summary_0` really is a dictionary of strings; both
grouped endpoints really do answer the numbered `serie_0` the rankings send, so one model serves both
families as intended; `GET /radar/bots/timeseries` answered its groups under the names the call had filtered
by, and `Group(name)` read them; and both verified-bot rankings answered entries, with each share of traffic
arriving as the string it is typed as. The two reads of a bot split the way the reference says they do —
`operatorUrl` and `userAgents` came on the single read and not on the list, and `signatureAgentUrl` came on
neither for the bot that run happened to read.

What the reference does *not* publish is five fields, and the run answered all five filled: `categories`,
because a bot can be filed under three categories at once where `category` carries only the first;
`accessLevel`, answered `DIRECT`; and `contentUse`, answered `immediate` in lower case where every category
beside it is upper case — those three on both reads — plus `documentationUrl` and `operatorWebsiteUrl` on the
single one. They are typed now, because the rule this library keeps to is that a field seen filled gets a
type and a field only ever seen named stays the JSON it arrived as. The four probes that take a per-group
limit first went out asking for one entry each and were answered `400 [2001]`, which is where the minimum
above came from; two of them then answered the incompatibility when they asked for two, and ask without it
now.

⛔ **Not wrapped:** `format=CSV` on any of these nine, for the reason every other radar read has — Cloudflare
replaces the envelope with a CSV body, which surfaces as an empty result rather than as an error. The single
bot read is wrapped but deliberately left out of the CI reach matrix: a probe needs a URL that answers the
same whatever account it runs against, and any slug could be one radar has retired, which would put a `404` in
the matrix that says nothing about reach. The live fact reads it with a slug the list beside it returned.

---

## Cross-cutting behaviour

| Concern | Where |
| --- | --- |
| Envelope (`success`, `errors`, `messages`, `result`, `result_info`) | `CloudflareResponse<T>`, `CloudflareRawResponse` |
| Pagination | `CloudflarePage<T>`, `CloudflareResultInfo`, `CloudflarePagedParameters`, `CloudflareCursorParameters` |
| Query strings | `QueryStringBuilder` (`Uri.EscapeDataString` for values; `.`/`[`/`]` left readable in names) |
| URL construction | `CloudflareUrl`, `CloudflareRequest` |
| Bodies | `JsonRequestBody`, `TextRequestBody`, `BinaryRequestBody`, `MultipartRequestBody` - whose parts go out with **quoted** `name` and `filename`, because .NET leaves a token-safe value bare and Cloudflare's AI endpoints answer that with 400 and error 6005 |
| Serialization | `CloudflareJson.Default`, `CloudflareValueAttribute`, `CloudflareEnumConverter<T>`, `TolerantBooleanConverter` (a flag Cloudflare wrote as a number or a word), `CloudflareTimestamps` (the three ways it writes a timestamp: ISO 8601, a space where the standard puts a `T`, and Go's `+0000 UTC` tail), `WorkerSecretBulkRequestConverter` (writes a bulk secret write by hand, because a null in that body is a deletion this writer would otherwise omit) |
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
