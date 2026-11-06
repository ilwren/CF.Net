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
| `ListVersionsAsync(scriptName, WorkerVersionListParameters?, accountId)` | `GET …/workers/scripts/{script_name}/versions` | newest first. Takes `page`, `per_page` and `deployable` (documented as ignoring pagination), but answers `{"items":[…]}` with **no** `result_info`, so it returns `WorkerVersionListResult` rather than `CloudflarePage<T>`. A version is `{id, number, metadata, annotations}`. The list page declines to name three of metadata's fields, but the versions model page publishes them: `source`, whose eleven values `WorkerVersionSources` lists, `modified_on`, and `hasPreview` - which a live answer spells `has_preview`, and that is the spelling the model reads. `annotations` is published by no version schema at all yet arrives anyway (`workers/triggered_by: upload`), and is the shared `WorkerAnnotations` type; an account-owned token uploads with an **empty** `author_email`. Anything still unnamed is captured in `AdditionalData`, and the integration test reports it only while there is something to report |
| `GetVersionAsync(scriptName, versionId, accountId)` | `GET …/workers/scripts/{script_name}/versions/{version_id}` | the list's `id`/`number`/`metadata` plus `resources` - that version's `bindings`, its `script` (`etag`, default `handlers`, `last_deployed_from`, `named_handlers`) and its `script_runtime` (`compatibility_date`, `compatibility_flags`, `limits.cpu_ms`, `migration_tag`, `usage_model`, `exports`). `null` on 404. The only call that shows a version's runtime as uploaded, since script settings describe the script rather than a version. `exports` stays a raw map: the reference types each entry as a union of six variants covering Durable Object lifecycle and transfer states, and the integration run reports what actually arrives instead of this library modelling it sight unseen |
| `CreateVersionAsync(scriptName, UploadWorkerScriptRequest, bool? bindingsInheritStrict, accountId)` | `POST …/workers/scripts/{script_name}/versions` | "Upload a Worker Version **without deploying** to Cloudflare's network" - the same `multipart/form-data` bundle as `UploadAsync` (one `metadata` part plus a part per module, built by the same private helper), but it creates a version and leaves the deployment serving what it was. **Module syntax only**: `main_module` is published as required here and there is no `body_part` alternative, so a service-worker-syntax request is rejected locally. `bindings_inherit=strict` turns an `inherit` binding that cannot be resolved against the latest uploaded version into error **10057** instead of letting Cloudflare silently drop it. Needs **Workers Scripts Write**, narrower than the read permission the other version endpoints accept. The response is the version in full plus two fields the version read does not publish: `startup_time_ms` and `exports_reconciliation`, the latter kept raw because all nine of its fields describe Durable Object migration outcomes this library cannot yet exercise. `metadata.annotations` is published here with **eight keys** - alias, tag, message and five CI details (commit SHA, repository URL, PR number/title/URL) - so `WorkerAnnotations` now carries those plus the `workers/triggered_by` only the deployment schema names; the maxima are documented but not enforced, because Cloudflare truncates a long `workers/message` rather than refusing it |
| `ListSchedulesAsync(scriptName, accountId)` | `GET …/workers/scripts/{script_name}/schedules` | the script's cron triggers. No query parameters, and `{"schedules":[{cron, created_on, modified_on}]}` - the array nested inside `result` with no `result_info`, the **third** Workers endpoint in a row that `CloudflarePage<T>` cannot describe. A script with no triggers answers with an empty array rather than an error |
| `UpdateSchedulesAsync(scriptName, IEnumerable<string> cronExpressions, accountId)` | `PUT …/workers/scripts/{script_name}/schedules` | **replaces the whole set** - a trigger not in the list is removed, so one expression sent to a script that has three leaves it with one. An empty list clears every trigger. The body is a **bare JSON array** of `{"cron": …}` objects rather than an object holding one, which is why the parameter is the expressions and not a request model. Needs **Workers Scripts Write** where the read needs only Read. Blank and duplicate expressions are refused locally; the grammar itself is left to Cloudflare, since guessing at it would reject expressions the API accepts. `created_on`/`modified_on` are Cloudflare's to write and are not sent. **An account prerequisite, not a permission**: with no `workers.dev` subdomain Cloudflare refuses the `PUT` with a 403 carrying **10063** "You need a workers.dev subdomain in order to proceed", even for a token whose Workers Scripts Write the same run has just proved by uploading and versioning a script. Opening Workers & Pages in the dashboard once creates the subdomain; the integration run reports 10063 rather than failing on it. Once the subdomain exists the whole flow is verified live: the `PUT` answers 200 with both triggers - **in an order of Cloudflare's choosing**, which is why the suite compares the set and reports the sequence it got rather than asserting one - the `GET` reads them back with `created_on` and `modified_on` filled in and nothing this library fails to model, and an empty `PUT` clears them again |
| `GetSettingsAsync(scriptName, accountId)` | `GET …/workers/scripts/{script_name}/script-settings` | **not** `/settings` — a common mistake |
| `UpdateSettingsAsync(scriptName, WorkerScriptSettings, accountId)` | `PATCH …/workers/scripts/{script_name}/script-settings` | changes `logpush`, `observability`, `tags`, `tail_consumers` - the same four fields the GET answers with, so one type serves both. **A patch, not a replace**: only what is set is sent, and this library never writes a JSON `null`, so clearing `tags` or `tail_consumers` means an empty list. A request with nothing set is refused locally (it would be an empty body). Observability now types `redact_query_string`, `issues`, `logs` (`enabled`, `invocation_logs`, `destinations`, `head_sampling_rate`, `persist`) and `traces` (plus `propagation_policy` ∈ `authenticated`, `accept`), which the published schema requires for a PATCH and previously landed in `AdditionalData`. |
| `CreateTailAsync(scriptName, accountId)` | `POST …/workers/scripts/{script_name}/tails` | starts a tail (a live stream of the Worker's logs and exceptions). **Takes no body** - the script in the path is the whole request. Answers `{id, expires_at, url}`, where `url` is what a consumer connects to - live, `wss://tail.developers.workers.dev/{id}`, a WebSocket endpoint rather than a REST one. The events themselves never pass through this API. **A script holds one tail at a time**: a second create answers 200 with a new id and replaces the running one. |
| `ListTailsAsync(scriptName, accountId)` | `GET …/workers/scripts/{script_name}/tails` | lists the script's tails, and answers **an array** - empty (`"result": []`) for a script that is not being tailed, and 404 reads as empty too. The published schema describes this endpoint as answering one object like the create does; a live call says otherwise. The items carry the same three fields the create answers with (`id`, `expires_at`, `url`), so one `WorkerTail` serves both and only the object-vs-array difference is real. |
| `DeleteTailAsync(scriptName, tailId, accountId)` | `DELETE …/workers/scripts/{script_name}/tails/{id}` | stops a tail early. A tail also stops on its own at `expires_at`, so deleting it is not the only way one ends. |
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

⛔ The newer JSON-only flow (`POST /workers/workers` → `…/versions` → `…/deployments`), static asset
upload sessions, Workers for Platforms dispatch namespaces, query filters on the domain list, and Workers AI.

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
shares every other type with the request. The integration run logs how many of the echoed filters carried a
`kind`, so that hedge can be dropped if Cloudflare always sends one.

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

⛔ Unexercised live: an update that sends a destination `configuration`, which needs a receiver that answers
Cloudflare's preflight. The suite has none; a Worker of the account's own, uploaded and left answering 200,
would serve as one.

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
