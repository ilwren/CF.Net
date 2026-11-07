# Integration testing against the real Cloudflare API

CF.Net separates two kinds of tests, and the difference matters:

| | `tests/CF.Net.UnitTests` | `tests/CF.Net.IntegrationTests` |
| --- | --- | --- |
| Network | **never** — a stubbed `HttpMessageHandler`, or a replayed cassette | real `api.cloudflare.com` |
| Credentials | none | `CLOUDFLARE_*` variables, or a git-ignored `credentials.local.json` |
| Tag | none | `[Trait("Category", "Integration")]` |
| Target frameworks | `net8.0`, `net10.0` | `net8.0` |
| Runs on | every push and pull request | manual dispatch and a weekly schedule |
| Cost | milliseconds | your API quota, and real changes to your account |

This page is about the second column: how to supply credentials, locally and in GitHub Actions, and
what the tests do once they have them.

---

## 1. How the tests discover credentials

`CloudflareIntegrationTest` reads the process environment once per test:

| Variable | Required for | Notes |
| --- | --- | --- |
| `CLOUDFLARE_API_TOKEN` | everything | preferred scheme |
| `CLOUDFLARE_GLOBAL_API_KEY` + `CLOUDFLARE_API_EMAIL` | everything | legacy alternative; both must be present |
| `CLOUDFLARE_ACCOUNT_ID` | Workers, Pages, D1, KV, R2 | |
| `CLOUDFLARE_ZONE_ID` | DNS records, cache purge | |
| `CLOUDFLARE_IT_ENABLE_WRITES` | the lifecycle tests | must be exactly `true` |
| `CLOUDFLARE_IT_CASSETTE` | recording | name of the cassette file to write, see [section 6](#6-recording-a-cassette) |
| `CLOUDFLARE_IT_RECORD` | recording and replay | `true`/`record`/`1` records (implied by a cassette name); `replay` answers from the committed cassette instead of calling Cloudflare |
| `CLOUDFLARE_IT_REPORT` | nothing — reporting only | path of a file that collects one line per skipped test, for the CI summary; unset writes nothing anywhere |

These are the same variables `CloudflareClientOptions.FromEnvironment()` reads, and the same ones
`.github/workflows/integration.yml` injects — so there is no separate CI-only configuration path.

### Where each value comes from

**`CLOUDFLARE_API_TOKEN`** — the secret of an API token, created under
[dash.cloudflare.com/profile/api-tokens](https://dash.cloudflare.com/profile/api-tokens) →
**Create Token** → *Custom token*; [section 3](#3-creating-a-properly-scoped-token) walks through the
permissions. It is shown **once**, on the page after *Create Token* — lose it and you roll the token
and copy the new one. Shape: `cfut_` for a token owned by your user, or `cfat_` for one owned by the
*account* (usually the better choice for CI, because it does not depend on one person's user), each
followed by 40 characters and a checksum. Tokens issued before 2026 are unprefixed 40-character
alphanumeric strings and still work.

Check one without running anything:

```bash
curl -sS "https://api.cloudflare.com/client/v4/user/tokens/verify" \
  -H "Authorization: Bearer $CLOUDFLARE_API_TOKEN"
# {"result":{"id":"...","status":"active"},"success":true,"errors":[],
#  "messages":[{"code":10000,"message":"This API Token is valid and active"}]}
```

**`CLOUDFLARE_ACCOUNT_ID`** — 32 hexadecimal characters, for example
`023e105f4ecef8ad9ca31a8372d0c353`. Not the account *name*, and not a whole dashboard URL (the first
path segment of `dash.cloudflare.com/<id>` is the account id, but pasting the URL is the classic
mistake). Three ways to copy it:

1. any domain's **Overview** page → the **API** section near the bottom → copy next to **Account ID**;
2. anywhere in the dashboard press `Ctrl`/`Cmd`+`K` → type *Copy account ID*;
3. **Workers & Pages** → **Account Details** → copy next to **Account ID**.

**`CLOUDFLARE_ZONE_ID`** — also 32 hexadecimal characters, and specific to one domain: the zone the
DNS and cache-purge tests run against. Domain **Overview** → the **API** section → copy next to
**Zone ID**; the same panel shows the account id. The token also needs that zone in its *Zone
Resources*, or the id is right and every call still returns `10007`.

**`CLOUDFLARE_API_EMAIL`** — the address the Cloudflare account logs in with. Only used on the legacy
path, where it becomes the `X-Auth-Email` header that Cloudflare matches the key against.

**`CLOUDFLARE_GLOBAL_API_KEY`** —
[dash.cloudflare.com/profile/api-tokens](https://dash.cloudflare.com/profile/api-tokens) → the **API
Keys** tab → **Global API Key** → *View*, which asks for your password and second factor. Shape
`cfk_` plus 40 characters and a checksum, or a pre-2026 37-45 character lowercase hexadecimal key.
Account-wide and impossible to scope, which is why the suite treats it as the alternative.

**Reading the ids back through the API.** With a token allowed to list them:

```bash
curl -sS "https://api.cloudflare.com/client/v4/accounts?per_page=50" \
  -H "Authorization: Bearer $CLOUDFLARE_API_TOKEN" \
  | jq -r '.result[] | "\(.id)  \(.name)"'

curl -sS "https://api.cloudflare.com/client/v4/zones?per_page=50" \
  -H "Authorization: Bearer $CLOUDFLARE_API_TOKEN" \
  | jq -r '.result[] | "\(.id)  \(.name)  \(.status)"'
```

An empty `result` there means the token lacks a permission that lists accounts or zones (`Account
Settings — Read`, `Zone — Read`), not that you have none. The dashboard is always right, so when in
doubt copy from there.

**A wrong shape is reported, not guessed at.** `CFNet.TestKit.CredentialFormat` looks at each value as
the fixture reads it and prints one warning per problem — a quoted paste, a dashboard URL, an account
name where an id belongs, an API token sitting in the global-key variable — and the CI summary repeats
it. Shape only, and a credential is never echoed: whether it actually works is Cloudflare's answer,
and `The_configured_credentials_are_accepted` asks for it.

> **Never commit a credential.** GitHub scans every commit for Cloudflare credentials, and on a public
> repository Cloudflare verifies the checksum and **revokes the token automatically**. That is why
> `credentials.local.json` is git-ignored, why `credentials.example.json` ships with placeholders, and
> why a cassette redacts every credential header on its way to disk.

**Not everyone wants to export variables in every shell.** A git-ignored
`tests/CF.Net.IntegrationTests/credentials.local.json` works too, and environment variables still win,
so a CI secret can never be shadowed by a stray local file:

```json
{
  "apiToken": "v1.0-...",
  "accountId": "023e105f4ecef8ad9ca31a8372d0c353",
  "zoneId": "8d0c8239f88f98a8cb82ec7bb29b8556",
  "enableWrites": false
}
```

`apiToken`/`globalApiKey`/`apiEmail`/`accountId`/`zoneId`/`baseAddress`/`enableWrites`/`cassette`/`record`
are all accepted, in camelCase or as their `CLOUDFLARE_*` spelling. `credentials.example.json` is the
committed template; copy it, fill it in, and it stays out of git.

When a variable is missing the test **does not fail**. xunit v2 has no dynamic skip, so the fixture
prints `SKIPPED: <reason>` through `ITestOutputHelper` and returns early. A run with no credentials
at all is a fast, green no-op — which is exactly what you want on a pull request from a fork. It also
appends the reason to `CLOUDFLARE_IT_REPORT` when that names a file, which is how the GitHub Actions
summary can tell a green no-op apart from a green run that really talked to Cloudflare
(see [section 4](#what-a-run-reports-back)).

The client is built through the container — `AddCloudflare(...)`, or `AddCloudflareHttpClient(...)`
when a run is recording — so the integration suite also proves the dependency-injection registration
works against the live API.

---

## 2. Running locally

```bash
export CLOUDFLARE_API_TOKEN='v1.0-abc...'
export CLOUDFLARE_ACCOUNT_ID='023e105f4ecef8ad9ca31a8372d0c353'
export CLOUDFLARE_ZONE_ID='372e67954025e0ba6aaa6d586b9e0b59'

dotnet test tests/CF.Net.IntegrationTests \
  --filter "Category=Integration" \
  --logger "console;verbosity=detailed"
```

To include the lifecycle tests that create and delete resources:

```bash
export CLOUDFLARE_IT_ENABLE_WRITES=true
```

PowerShell:

```powershell
$env:CLOUDFLARE_API_TOKEN  = 'v1.0-abc...'
$env:CLOUDFLARE_ACCOUNT_ID = '023e105f4ecef8ad9ca31a8372d0c353'
$env:CLOUDFLARE_ZONE_ID    = '372e67954025e0ba6aaa6d586b9e0b59'
$env:CLOUDFLARE_IT_ENABLE_WRITES = 'true'

dotnet test tests/CF.Net.IntegrationTests --filter "Category=Integration"
```

Prefer not to put a token in your shell history? Copy the committed template and fill it in:

```bash
cp tests/CF.Net.IntegrationTests/credentials.example.json \
   tests/CF.Net.IntegrationTests/credentials.local.json
$EDITOR tests/CF.Net.IntegrationTests/credentials.local.json
dotnet test tests/CF.Net.IntegrationTests --filter "Category=Integration"
```

`credentials.local.json` is git-ignored, and environment variables still take precedence over it, so
a CI secret can never be shadowed by a stale local file. The same works with a `.env` you source
yourself:

```bash
set -a; . ./.env; set +a
dotnet test tests/CF.Net.IntegrationTests --filter "Category=Integration"
```

`.gitignore` in this repository excludes `.env`, `.env.*`, `secrets.json`, `credentials.json` and
`credentials.local.json`.

### Narrowing a run

```bash
# One test
dotnet test tests/CF.Net.IntegrationTests --filter "FullyQualifiedName~A_txt_record_can_be_created"

# Everything read-only
dotnet test tests/CF.Net.IntegrationTests --filter "Category=Integration&ClassName~ReadOnly"

# One product area only - the cheapest way to check a token's R2 permission, for instance
dotnet test tests/CF.Net.IntegrationTests --filter "Category=Integration&FullyQualifiedName~R2"
```

In GitHub Actions the workflow's **`filter`** input does the same thing: whatever you type is appended
to `Category=Integration&`, so `FullyQualifiedName~R2` runs only the R2 tests. It reaches
`dotnet test` through the environment, never by being interpolated into a shell.

---

## 3. Creating a properly scoped token

Cloudflare dashboard → **My Profile** → **API Tokens** → **Create Token** → **Custom token**.

For the full suite:

| Permission | Access | What it covers here |
| --- | --- | --- |
| User | API Tokens — Read | `GET /user/tokens/verify` |
| Zone | Zone — Read | the zone list, `GET /zones/{id}`, activation checks |
| Zone | DNS — Edit | DNS records, zone file export and import, **and DNSSEC** — DNSSEC has no permission group of its own, it rides on DNS |
| Zone | Zone Settings — Edit | `GET`/`PATCH /zones/{id}/settings`, including the SSL enrolment settings Cloudflare serves from that path |
| Zone | Cache Purge — Purge | `POST /zones/{id}/purge_cache` |
| Zone | Page Rules — Edit | `GET`/`POST`/`PUT`/`PATCH`/`DELETE /zones/{id}/pagerules`. **Zone — Edit** is accepted instead, and a plan only allows three page rules, so a create can be refused for a full zone rather than for the token. **No group helps an account-owned token**: Cloudflare answers `400 [1011]` "Page Rules endpoint does not support account owned tokens" to every call on this path, so page rules need a user-owned (`cfut_`) token or the global key |
| Zone | SSL and Certificates — Edit | certificate packs, Universal SSL, custom hostnames, the fallback origin, origin TLS client auth, **and the origin CA certificates at `POST`/`GET`/`DELETE /certificates`** - a root-level path with the zone as a filter, and one Cloudflare publishes no group of its own for, so the `origin_ca` probe is what says whether this one reaches them |
| Account | Account Settings — Read | `GET /accounts`, `GET /accounts/{id}` |
| Zone | Firewall Services — Edit | `GET`/`POST`/`PUT`/`DELETE /zones/{id}/filters` — the saved filters a firewall rule points at. Cloudflare documents the create as **deprecated** in favour of the ruleset engine, and a zone whose rules were all migrated may hold none |
| Account | Account Firewall Access Rules — Read **and** Write | `GET`/`POST`/`PATCH`/`DELETE /{accounts\|zones}/{id}/firewall/access_rules/rules` — IP Access rules. **Their own group**: Firewall Services does not cover them. A live run narrowed down which path actually wants it — one token created a rule on the **zone** path and listed it back, then was refused on the **account** path with `10000` — so the group is what the account scope needs, and the zone scope was reachable without it. Read `de7a688cc47d43bd9ea700b467a09c96`, Write `a416acf9ef5a4af19fb11ed3b96b1fe6` |
| Account | Workers Scripts — Edit | scripts, deployments, secrets, the `workers.dev` subdomain and custom domains |
| Zone | Workers Routes — Edit | `GET`/`POST`/`PUT`/`DELETE /zones/{id}/workers/routes`. The token catalogue files this group under the account, but a route lives in a zone, so a token whose *Zone Resources* do not name the zone gets a `10007 Route not found` that reads like a missing permission |
| Account | Account Settings — Edit | `PUT /accounts/{id}/workers/account-settings` — Cloudflare documents this write as needing **Account Settings Write** rather than a Workers group. A live run confirmed it: the read answers 200 and the write answers **403 `[10000]`** without that group. The fact reports the refusal instead of failing on it, and the group (`1af1fa2adc104452b74a9a3364202f20`) is in the token URL the workflow generates, so a token created from it verifies the write too |
| Account | Pages — Edit | projects, deployments, domains |
| Account | D1 — Edit | databases, queries, backups |
| Account | Workers KV Storage — Edit | namespaces, values, bulk writes |
| Account | Workers R2 Storage — Edit | buckets — **and R2 must be enabled on the account**, which is a separate one-time dashboard step; until then the API answers 403 whatever the token says |

Two rows are easy to get wrong. Cache purge is its own permission group — the dashboard calls it **Cache
Purge** at the level **Purge**, and `Zone Settings — Edit` does not grant it. And SSL for SaaS, custom
hostnames and origin TLS client auth all live under **SSL and Certificates**, not under Zone or DNS, so a token
built for "just DNS" skips every one of them with a 403 that reads like a bug in the client.

### Creating that token from a URL

Cloudflare pre-fills the token form from a query string, so the whole table above is one click rather than
fifty. `permissionGroupKeys` is a URL-encoded JSON array of `{"key": …, "type": …}` objects, where `type` is
`read`, `edit`, `purge`, `run` or `revoke`:

```txt
https://dash.cloudflare.com/?to=/:account/api-tokens&permissionGroupKeys=%5B%7B%22key%22%3A%22dns%22%2C%22type%22%3A%22edit%22%7D%5D&name=CF.Net%20integration
```

Each `key` is either a **short key** or a **permission group id**, and the two behave differently:

| | Short key | Permission group id |
| --- | --- | --- |
| Account token URL | resolved; a key the dashboard cannot resolve is **named in a notice** in the form | resolved, and `type` is ignored because an id names exactly one group |
| User token URL | resolved only when it matches a group name exactly; anything else is **dropped with no notice** | **not supported** |
| Coverage | about 28 groups, two of them mislabelled against the dashboard: Pages is `page`, R2 is `workers_r2` | every group Cloudflare has |

So the URLs this repository generates are built from **ids** — which is also the only way to carry the
groups the next modules need, because none of them has a short key at all:

| Group | Scope | Id |
| --- | --- | --- |
| Account Rulesets Read / Write | account | `fb39996ee9044d2a8725921e02744b39` / `56907406c3d548ed902070ec4df0e328` |
| Account Rule Lists Read / Write — the dashboard's *Filter Lists* | account | `4f1071168de8466e9808de86febfc516` / `2edbf20661fd4661b0fe10e9e12f485c` |
| Mass URL Redirects Read / Write — *Bulk URL Redirects* | account | `429a068902904c5a9ed9fc267c67da9a` / `abe78e2276664f4db588c1f675a77486` |
| Dynamic URL Redirects Read / Write | zone | `d8e12db741544d1586ec1d6f5d3c7786` / `74e1036f577a48528b78d2413b40538d` |
| Workers Tail Read | account | `05880cd1bdc24d8bae0be2136972816b` |
| Workers Observability Read / Write | account | `66c1ed49f4ed46098b75696a6d4ee3c9` / `82c075da3f4647a2a03becd0fe240f8a` |
| Workers AI Read | account | `a92d2450e05d4e7bb7d0a64968f83d11` |
| Turnstile Sites Read / Write | account | `5d78fd7895974fd0bdbbbb079482721b` / `755c05aa014b4f9ab263aa80b8167bd8` |
| Waiting Rooms Read / Write | zone | `cab5202d07ef47beae788e6bc95cb6fe` / `24fc124dc8254e0db468e60bf410c800` |
| Account Waiting Rooms Read | account | `3e4e20ee40b9475dae22201c468fdb70` |
| Load Balancing: Monitors and Pools Read / Write | account | `9d24387c6e8544e2bc4024a03991339f` / `d2a1802cc9a34e30852f8b33869b2f3c` |

The whole list — 51 groups, everything this suite touches today plus all of the above — is `WANTED_GROUPS`
in `.github/workflows/integration.yml`. Every run builds both URLs from it and prints them in the step
summary under *Create the token without ticking boxes*: one with every group, one with the read-only half
(which still carries Cache Purge, because the reachability probe reads that permission with a body
Cloudflare refuses, so nothing is purged by having it). Wrapping a new module means adding a line to that
list, and the URL a reader copies afterwards already carries it.

Ids are the same values `GET /accounts/{account_id}/tokens/permission_groups` returns, that `POST
/user/tokens` takes and that the Terraform provider uses, so they are stable rather than guessed — and on
any run whose token can read that endpoint, the step cross-checks the list against the live catalogue and
reports drift as a notice. It does run against this account: the token answers with a catalogue of 415
groups and the notice says all 51 ids are in it, names included. It did not always — a token holding only
*Account API Tokens — Read* was refused here with `9109`, and the one holding *Write* as well answers,
which is why the URL asks for Write. `User.ListAccountPermissionGroupsAsync` wraps the same endpoint, and
its integration test checks the live catalogue against the categories and scopes this library publishes as
constants, so a group Cloudflare adds in a new category is reported rather than quietly read as `other`.

Two things no URL can pre-fill: **Account Resources** and **Zone Resources**. The zone-scoped groups — Zone,
DNS, Zone Settings, SSL and Certificates, Cache Purge, Analytics, Firewall Services, Page Rules, Dynamic URL
Redirects, Waiting Rooms, Workers Routes — grant nothing until the form lists the zone, and a zone missing
from Zone Resources is the `10007 Route not found` that skips every zone test. The user-token URL is the one
that takes `accountId=*` and `zoneId=all`, and since it resolves short keys only, it can carry the probe
list and none of the groups in the table above.

The same URLs are printed by the *Check what the credentials can actually reach* step whenever it finds a
permission missing, so a refused run tells you what to click.

**Client IP address filtering**: leave it empty for CI. GitHub-hosted runners use a large, changing
pool of egress addresses, and an IP allowlist will make every run fail with a 403 that looks like a
bad token.

**TTL**: set one. A CI token that never expires is a standing risk.

For read-only runs take **Read** instead of **Edit** on every row and keep Cache Purge only if you want the
purge probes to run. That is the set the Monday schedule uses, and it cannot change your account — the
`ReadOnlyIntegrationTests` class still exercises everything it can.

**Resources are as important as permissions.** Under *Account Resources* include the account you are
testing; under *Zone Resources* include that one zone (or *All zones*). A token with the right
permissions but no zone resource returns `10007 Route not found` for a zone you can see perfectly well
in the dashboard, which reads like a bug in the client and is not.

This is common enough that the workflow checks for it before running anything. The step named *Check what
the credentials can actually reach* makes one `curl` call per product area — thirty-five of them, covering
zones, DNS records, DNSSEC and its ZSK, zone settings, certificate packs, custom hostnames, origin TLS
client auth, origin CA certificates, cache purge, the account, Workers scripts, domains and routes, Pages,
D1, KV, R2, rulesets, filter lists, page rules, firewall filters, IP Access rules, Turnstile, load
balancing, Workers AI and account tokens — with the same
credentials, and reports each answer as `label=status[shape]`:

```
Reach matrix: zones=200[1 item(s)] zone=200[object, 22 field(s)] dns=200[0 item(s) - EMPTY]
  settings=200[56 item(s)] custom_hostnames=403[success=false 1404: No quota has been allocated…]
  cache_purge=400[success=false 1012: Request must contain one of "purge_everything"…] r2=403[…]
```

The shape is the point. A status code alone cannot tell "readable" from "readable and empty", and a 200
carrying `"result": null` is a third thing again — which is what makes the matrix worth more than the test
results when something is misconfigured. From it, three different refusals are told apart:

| What it prints | What it means |
| --- | --- |
| `GET /zones succeeded but returned no zones` | the token authenticates but has no Zone Resources, or the zone belongs to a different account. Every zone-scoped test will skip — DNS records, DNSSEC, zone settings, SSL, custom hostnames, origin TLS, cache purge — and the run can be green without having verified any of them |
| `CLOUDFLARE_ZONE_ID was refused (or not found)` | that zone is not in the token's Zone Resources, or is not in this account. Cloudflare answers 403 for both, and answers 200 with a null result for some out-of-scope zones, which the library reports as `null` the same way |
| `N probe(s) were refused, covering M missing permission(s)` | the permission really is absent. The step summary carries a token-creation URL that pre-fills every permission this suite needs, so the fix is one click rather than a form |
| `N probe(s) refused while their permission group read fine elsewhere` | a paid feature that is not switched on, not a token problem. Observed live: `custom_hostnames` answers `1404 No quota has been allocated for this zone` while certificate packs and origin TLS settings in the same *SSL and Certificates* group read fine — that is SSL for SaaS not being enabled |
| `R2 answered 403` | either the R2 permission is missing or R2 is not enabled on the account (`10042`). Reported as a notice, not an error, because the R2 tests skip with a reason by design |
| a read straight after a write answers as though nothing happened | Cloudflare accepts the write with a 200 and the path beside it catches up a moment later: `404 [10007] This Worker does not exist on your account` for a just-uploaded script's secrets, an empty `GET …/workers/routes` a call after a route's create had answered its id, and the same in reverse for a delete. The write facts read again through `ReadUntilAsync` rather than fail. This is not the `10007` the matrix reports for a missing `workers.dev` subdomain - same code, two meanings |
| `404 [10404] No route for that URI` | the path does not exist — neither a permission nor an entitlement, and no scope on a token fixes it. Found live on `/accounts/{id}/origin_ca_certificates`, the path that resource is named after; the real one is `/certificates` at the API root |
| `PUT …/schedules was refused with [10063]` | cron triggers need the account to have a `workers.dev` subdomain and this one does not — the same reason `GET …/workers/subdomain` answers 404 and the subdomain test skips with `10007`. Reported rather than asserted: the token's *Workers Scripts Write* is proved by the upload, version create and deployment read in that very test, so nothing about the credentials explains the refusal. Opening Workers & Pages in the dashboard once creates the subdomain, after which the step writes, reads back and clears two triggers for real. That is what the next run did: `GET …/workers/subdomain` answered 200, the `PUT` answered 200 with both triggers, and the cron flow is verified end to end |

A probe that answers 4xx for a reason that is *not* a refusal still proves the permission is there: cache
purge is probed with an empty body, so `400 [1012 Request must contain one of "purge_everything"…]` means
*Cache Purge — Purge* is granted and nothing was purged. Any 2xx counts as readable, which matters because
`GET …/origin_tls_client_auth/settings` answers **202**, not 200.

Four codes seen live mean *the account is missing something*, not that the token is — and each is reported
rather than failed, because a credential change would not fix any of them: **1404** (SSL for SaaS is not
enabled, on `custom_hostnames`), **10042** (R2 is not enabled), **10007** (no `workers.dev` subdomain, on
`workers/subdomain`) and **10063** (cron triggers, which need that same subdomain). The last two are one
gap with two symptoms, and opening Workers & Pages in the dashboard once clears both — which was then
seen to happen: a later run read `workers/subdomain` as 200 and wrote cron triggers for real. **1404** and
**10042** are still standing, because they need a paid feature switched on rather than a page opened.

The step never fails the job — a repository with no credentials yet is still allowed to be green — so read
the annotations rather than the conclusion. Only the annotations, in fact: the step writes its table and the
token URLs to `$GITHUB_STEP_SUMMARY`, which is visible in the run's UI but not through the API.

**Account-owned tokens.** *Manage Account* → **API Tokens** creates a token owned by the account
(`cfat_…`) rather than by your user. For CI that is usually the better choice: it does not stop working
when somebody leaves, and it is easier to hand over. Not every endpoint accepts one — Cloudflare
publishes a compatibility matrix — and the two tests that read `/user` will tell you if yours does not.

**Creating it programmatically.** `GET /user/tokens/permission_groups` lists every permission with the
id the API wants, and `POST /user/tokens` creates the token. Useful when the token itself has to be
provisioned by automation; the secret is returned once, in that response.

> Never use the **Global API Key** in CI. It is account-wide, cannot be scoped, and rotating it
> invalidates every other integration at once. CF.Net supports it because real systems still depend
> on it; a dedicated token is strictly better here.

---

## 4. GitHub Actions

`.github/workflows/integration.yml` is the reference implementation.

### Add the secrets

GitHub → repository → **Settings** → **Secrets and variables** → **Actions** →
**New repository secret**:

| Secret name | Value |
| --- | --- |
| `CLOUDFLARE_API_TOKEN` | the token from step 3 |
| `CLOUDFLARE_ACCOUNT_ID` | account id from the dashboard sidebar |
| `CLOUDFLARE_ZONE_ID` | zone id from the dashboard sidebar |
| `CLOUDFLARE_API_EMAIL` | *(optional)* only for the legacy path |
| `CLOUDFLARE_GLOBAL_API_KEY` | *(optional)* only for the legacy path |

Use **repository secrets**, not environment secrets, unless you also want required reviewers — in
which case create an environment named e.g. `cloudflare` and attach the secrets there, then add
`environment: cloudflare` to the job.

### How the workflow uses them

```yaml
env:
  CLOUDFLARE_API_TOKEN: ${{ secrets.CLOUDFLARE_API_TOKEN }}
  CLOUDFLARE_ACCOUNT_ID: ${{ secrets.CLOUDFLARE_ACCOUNT_ID }}
  CLOUDFLARE_ZONE_ID: ${{ secrets.CLOUDFLARE_ZONE_ID }}
  CLOUDFLARE_API_EMAIL: ${{ secrets.CLOUDFLARE_API_EMAIL }}
  CLOUDFLARE_GLOBAL_API_KEY: ${{ secrets.CLOUDFLARE_GLOBAL_API_KEY }}
  CLOUDFLARE_IT_ENABLE_WRITES: ${{ inputs.enable_writes && 'true' || 'false' }}
  CLOUDFLARE_IT_CASSETTE: ${{ inputs.cassette }}
  CLOUDFLARE_IT_RECORD: ${{ inputs.cassette && 'true' || 'false' }}
```

One more variable is set inside the steps that need it, from the runner's own `$RUNNER_TEMP`:

```bash
export CLOUDFLARE_IT_REPORT="${CLOUDFLARE_IT_REPORT:-$RUNNER_TEMP/integration-report.tsv}"
```

It is deliberately *not* a workflow- or job-level `env:` entry: the `runner` context is only available
to step-level `env`, and referencing it higher up makes GitHub reject the entire file with
`Unrecognized named-value: 'runner'` — which shows up as a red run with no jobs at all, on every push.

Job-level `env:` is what makes them visible as process environment variables to `dotnet test`.
GitHub masks every secret value in all log output automatically, and the tests never print a
credential either — `CloudflareIntegrationTest` logs the account id, zone names and CF-RAY values,
not tokens.

### Triggers, and why not `push`

> **Temporary exception, in the file right now.** `workflow_dispatch` and `schedule` only fire for a
> workflow that exists on the **default** branch, and this one lives on a feature branch until its pull
> request is merged — so the Actions page lists it with no *Run workflow* button, and the Monday cron has
> never fired. While that is true the file also carries `on: push: branches: [<that one branch>]`, plus a
> matching `github.event_name == 'push'` clause in the `CLOUDFLARE_IT_ENABLE_WRITES` expression so a push
> run exercises write operations too (a push carries no dispatch inputs). Both are marked TEMPORARY in the
> file and both get deleted in one commit at the merge; everything below describes the design it returns to.

```yaml
on:
  workflow_dispatch:
    inputs:
      enable_writes:
        description: 'Allow tests that create and delete resources'
        type: boolean
        default: false
      cassette:
        description: 'Record into tests/cassettes/<name>.json'
        type: string
        default: ''
      commit_cassette:
        description: 'Commit the recorded cassette back to this branch'
        type: boolean
        default: false
      filter:
        description: 'Narrow the run, e.g. FullyQualifiedName~R2'
        type: string
        default: ''
  schedule:
    - cron: '0 4 * * 1'
```

- **`workflow_dispatch`** — a human decides when real API calls happen, and whether writes are
  allowed. This is the path to use while developing.
- **`schedule`** — a weekly read-only smoke test. Scheduled runs never receive `inputs`, so the
  expression above resolves to `false` and no writes can happen unattended.
- **deliberately not `push` / `pull_request`** — secrets are not available to workflows triggered by
  a pull request from a fork, and even for branch pushes it means every commit spends quota and can
  mutate your account. `ci.yml` already proves the whole solution compiles, including the
  integration project.

### Running it

GitHub → **Actions** → **Integration** → **Run workflow** → choose the branch → optionally tick
*Allow tests that create and delete resources*, or name a cassette to record → **Run workflow**.

The first step reports what it found:

```
##[notice]Using CLOUDFLARE_API_TOKEN.
##[warning]CLOUDFLARE_ZONE_ID is not set; DNS and cache purge tests will skip.
Write operations: false
```

With no credentials at all it emits a warning and finishes green, printing
`Integration tests skipped - no credentials`. That is intentional: a repository without secrets
should not have a permanently red workflow.

Results are uploaded as the `integration-results` artifact (a `.trx` you can open in Visual Studio
or with `trx2junit`). A recording run also uploads `cassette-<name>` and, with *Commit the recorded
cassette* ticked, pushes the file to the branch it ran on.

### What a run reports back

Three things keep a run readable without opening the raw log.

1. **`Verify the credentials against the live API`** — before anything is restored, the workflow calls
   `GET /user/tokens/verify` (or `GET /user` for the legacy global-key pair) with `curl` and fails the
   job straight away if Cloudflare rejects the secret, quoting Cloudflare's own error code and
   message. That collapses "25 tests failed with `9109`" into one line. The call is made outside the
   library on purpose, so it is *not* captured in a cassette; if the runner cannot reach the API at
   all, the step warns and lets the tests have their say.
2. **`Summarise the run`** — parses the `.trx` into an outcome table on `$GITHUB_STEP_SUMMARY`, with
   the message of every failing test underneath it.
3. **The skip table** — xunit v2 cannot skip a test at run time, so a test with nothing to do ends
   early and is reported as *passed*. To stop that looking like a success, the fixture appends one
   line per skip to `CLOUDFLARE_IT_REPORT` and the summary renders them:

   | Test | Why |
   | --- | --- |
   | `ReadOnlyIntegrationTests.Zones_can_be_listed_and_read` | no Cloudflare credentials found (…) |
   | `WriteIntegrationTests.An_r2_bucket_can_be_created_and_removed` | `CLOUDFLARE_IT_ENABLE_WRITES` is not 'true'. |

   If that table is not empty, a green tick means "there was nothing to verify", not "verified".

**A response CF.Net cannot read.** One failure mode is neither a credential problem nor a Cloudflare
error, and it is worth recognising because it used to hide: a 2xx whose body is shaped like an envelope but
does not fit the model. The transport throws `CloudflareApiException` whose message puts the two things a
reader can act on first — the failing JSON path, such as `$.result[0].latest_deployment.source`, and a
quotation of the body around it, the failing line marked `>>` — and only then explains that a mismatch is
reported as an error rather than as a `null` result. That order is deliberate: a CI run surfaces the
message as a build annotation, which is cut short, and the explanation is the part that can afford to be
lost. The body Cloudflare sent in full is on `ResponseBody`. Before
that, the same situation produced a `null` result and a `Success` taken from the status code, so a zone that
`curl` could read in full was reported by the suite as unreadable and every test that needed it skipped or
failed on an assertion that said nothing. Two helpers in the fixture recognise this case
(`InnerException is JsonException`) and report it once instead of failing every test that shares the
precondition the same way: the zone-scoped one, as a skip reason, and `ReadablePagesProjectsAsync`, which
also publishes the quotation through `Warn()` — the channel whose text reaches a run's annotations in
full, where a skip reason is cut to 280 characters and only the first five are kept. It was written for a
real case: `GET …/pages/projects` answered 200 with a `latest_deployment.source` that was not a string,
which failed three Pages tests at once with the same 300-character message and no clue what the value
was. Quoted, the value turned out to be `"source": { "type": "github",` — a project connected to a
repository, where a Direct Upload project has no source at all — and `PagesDeployment.Source` now reads
the `PagesProjectSource` object Cloudflare sends. That is the whole loop in one paragraph: a strict
parser notices, the message quotes, the model is corrected, and the tests that skipped start verifying. Blank strings are partly handled: Cloudflare spells "no value" as `""` in places, and
`BlankStringAsNullConverterFactory` reads those as null for the nullable date, time and GUID types.
Numbers and booleans are deliberately left out, because a custom converter for them would disable
`JsonNumberHandling.AllowReadingFromString`, which is what reads the numbers Cloudflare sends as strings
(`"file_size": "12345"`) — a worse trade than the blank string it would absorb. With
`ThrowOnApiError = false` the same unparseable 2xx is returned rather than thrown, with the body on
`RawBody`.

The writer is `CFNet.TestKit.RunReport`, and its file format is pinned down by offline unit tests in
`tests/CF.Net.UnitTests/Testing/RunReportTests.cs`. With `CLOUDFLARE_IT_REPORT` unset — the normal
case at a terminal — it writes nothing anywhere, and a write that fails is swallowed rather than
turning a passing test into a failing one.

The same report carries a **transcript of every Cloudflare call the run made**, because the fixture
hooks `CloudflareClientOptions.InspectResponse`:

| Call | Result |
| --- | --- |
| `GET /client/v4/zones?per_page=50` | `200 OK` |
| `POST /client/v4/accounts/…/storage/kv/namespaces` | `200 OK` |
| `DELETE /client/v4/accounts/…/storage/kv/namespaces/…` | `200 OK` |

It appears collapsed in the summary, and only the method, path, query and status are written —
credentials live in headers, so nothing secret can reach the report. Use it to confirm a run really
touched the account, to see that a write run deleted what it created, and to spot retries as the
repeated call they are.

---

## 5. What the tests actually do

### `ReadOnlyIntegrationTests`

Always safe. They run whenever credentials are present.

| Test | Asserts |
| --- | --- |
| `The_configured_credentials_are_accepted` | `CheckConnectionAsync()` succeeds and is fast |
| `A_token_can_be_verified_directly` | `/user/tokens/verify` reports the token active |
| `The_authenticated_user_can_be_read` | `/user` returns an id |
| `Accounts_can_be_listed` | `/accounts` returns items, and contains the configured account |
| `Zones_can_be_listed_and_read` | every zone has id/name/status; the configured zone resolves |
| `Every_zone_page_can_be_walked` | the `ListAllAsync` page loop terminates |
| `Dns_records_can_be_listed` | every record has id/type/name |
| `Dns_records_can_be_filtered_by_type` | the `type` query filter is honoured server-side |
| `An_unknown_zone_reports_not_found…` | `GetAsync` returns `null` instead of throwing |
| `An_unknown_dns_record_reports_not_found` | same, for DNS |
| `Worker_scripts_can_be_listed` | `/workers/scripts` |
| `The_workers_dev_subdomain_can_be_read` | `/workers/subdomain` |
| `Pages_projects_can_be_listed` | `/pages/projects` |
| `Pages_deployments_logs_and_upload_tokens_can_be_read` | `/pages/projects/{p}/deployments?env=production`, `…/deployments/{id}/history/logs` (a 404 is a warning, not a failure - a Direct Upload deployment never runs a build), `…/upload-token` logged as its length only because the JWT is a credential, and `…/domains/{name}` for a domain that was never attached reading as `null` |
| `D1_databases_can_be_listed_and_queried` | `/d1/database`, plus `SELECT 1` and `FirstColumn<int>` |
| `Kv_namespaces_can_be_listed` | `/storage/kv/namespaces` |
| `R2_buckets_can_be_listed` | `/r2/buckets`, including the `{buckets:[…]}` unwrap |
| `Ssl_certificate_packs_and_the_enrolment_settings_can_be_read` | `/ssl/certificate_packs` with `per_page`, `/ssl/universal/settings`, and the three enrolment settings Cloudflare files under SSL - `ssl/auto_origin_tls_kex`, `ssl/automatic_mode` and `ssl/origin_tls_compliance_modes` |
| `Custom_hostnames_can_be_listed_and_the_fallback_origin_read` | `/custom_hostnames` with its filters, and `/custom_hostnames/fallback_origin`, whose own `errors` array of plain strings is read back inside a successful response |
| `Origin_tls_client_auth_settings_and_certificates_can_be_read` | `/origin_tls_client_auth/settings`, `…/hostnames/certificates`, and the deprecated zone-level list - asserted to log the private key's length and never the key |
| `Dnssec_state_can_be_read_without_changing_it` | `/dnssec`, read-only on purpose: enabling would leave the zone `pending` on a DS record no test run can publish at the registrar, and disabling a signed zone would break resolution |
| `Dnssec_zone_signing_keys_can_be_listed` | `/dnssec/zsk`, the one call that proves the PascalCase payload against the real API rather than a fixture; an unsigned zone answering with an empty list is a normal result |
| `Every_product_area_is_reachable_through_the_client_facade` | every service the client exposes, wired to one transport |
| `An_insufficiently_scoped_token_is_reported_as_an_authentication_failure` | 403/404 map onto `IsAuthenticationFailure` / `IsNotFound` |

### `WriteIntegrationTests`

Gated behind `CLOUDFLARE_IT_ENABLE_WRITES=true`. Each one cleans up in a `finally` block, so an
assertion failure mid-test still removes the resource.

| Test | Creates | Removes |
| --- | --- | --- |
| `A_txt_record_can_be_created_read_updated_and_deleted` | a TXT record in your zone, TTL 120 | `DELETE` the record |
| `A_kv_namespace_and_value_can_be_created_and_removed` | a namespace, a value, a bulk write, a rename | `DELETE` the namespace |
| `An_r2_bucket_can_be_created_and_removed` | a bucket with a location hint | `DELETE` the bucket |
| `A_d1_database_can_be_created_queried_and_removed` | a database, a table, two rows | `DELETE` the database |
| `A_worker_script_can_be_uploaded_and_removed` | a script with a `plain_text` binding | `DELETE` the script |
| `A_pages_project_can_be_created_and_removed` | a project on branch `main` | `DELETE` the project |
| `The_cache_can_be_purged_by_url` | nothing | n/a — purges one URL that does not exist |
| `A_pages_project_build_cache_can_be_purged` | nothing | n/a — empties cached build artefacts, so the next build is slower |
| `Dns_records_can_be_created_and_deleted_in_one_batch` | two TXT records, in one batch call | the same batch endpoint's deletes group |
| `An_observability_destination_can_be_created_and_removed` | a telemetry destination, then a patch of it | `DELETE` the destination |
| `An_observability_live_tail_can_be_prepared_and_kept_alive` | nothing — prepares a live tail and heartbeats it | n/a — a live tail expires on its own |
| `A_worker_script_settings_can_be_updated` | a script, then a patch of its settings | `DELETE` the script |
| `A_worker_tail_can_be_started_and_stopped` | a script and a tail on it | `DELETE` the tail, then the script |
| `A_redirect_ruleset_can_be_created_given_a_bulk_redirect_rule_and_removed` | a redirect list with items, a ruleset and a rule in it, with the zone's entrypoint pointed at it | the rule, the ruleset, the list, and the entrypoint put back |
| `A_rules_list_can_be_created_filled_and_removed` | a list, its items, and a change to its description | the items, then `DELETE` the list |
| `A_zone_setting_can_be_changed_and_changed_back` | nothing — switches one zone setting | n/a — the setting is switched back |
| `A_turnstile_widget_can_be_created_read_updated_rotated_and_deleted` | a widget, then a `PUT` of it and a rotated secret | `DELETE` the widget |
| `A_worker_secret_can_be_written_listed_read_bulk_written_and_removed` | a script with a `secret_text` binding, a secret written onto it, and a bulk write that adds one and removes the other | the secret, then `DELETE` the script |
| `A_worker_route_can_be_created_read_changed_and_removed` | a script, a proxied A record in your zone, a route on that hostname, and two `PUT`s of it - one that moves the pattern, one that takes the script off | the route, the record, then `DELETE` the script |
| `An_origin_ca_certificate_can_be_requested_listed_read_and_revoked` | a 2048-bit RSA key **generated inside the test**, a CSR for a generated hostname in your zone, and the certificate Cloudflare's CA signs for it (one year) | `DELETE` revokes it - and again in the `finally`, so a failure mid-test cannot leave a signed certificate behind. The private key never leaves the test process |
| `A_firewall_filter_can_be_created_listed_read_changed_bulk_changed_and_removed` | two saved filters in one `POST` (one of them paused), a `PUT` of one and a bulk `PUT` of both — **skipped when Cloudflare answers `410 [10020] filters.api.deprecated`, which is what it does for an account whose filters API has been retired** | the single `DELETE`, then the bulk `DELETE` with a body |
| `An_ip_access_rule_can_be_created_listed_read_changed_and_removed` | one IP Access rule in **your zone** blocking `192.0.2.1` — documentation space, which routes nowhere — then a `PATCH` to one of the modes Cloudflare offered. It also reads the **account-scoped** list of the same resource, and a refusal there is reported rather than failing the fact | `DELETE` the rule |
| `A_page_rule_can_be_created_read_patched_replaced_and_removed` | one page rule on a generated hostname, then a `PATCH` of its status and a `PUT` that turns it into a redirect — **skipped for an account-owned token**, which Cloudflare refuses this endpoint for with `1011` | `DELETE` the rule - which also frees the allowance slot it used |

Resource names are generated with a timestamp and a random suffix (`cfnet-it-20260101120000-a1b2c3d4`)
so concurrent runs never collide, and a failed cleanup leaves something obviously disposable.

**Costs to be aware of.** All of these sit inside Cloudflare's free tier for a small number of runs,
but a Worker script counts towards your script limit, a D1 database towards your database limit, an
R2 bucket towards your bucket limit, and a cache purge is rate limited (30 per 15 minutes for
`purge_everything`, 100 per 15 minutes otherwise). Run the write suite deliberately, not on a
schedule.

Two Pages calls are deliberately **not** exercised live. `RetryDeploymentAsync` starts a real build, which
costs build minutes for no additional evidence that the endpoint works; and the Direct Upload asset endpoints
are not in the published API reference at all, so there is nothing documented to verify against - see the
Pages section of `api-coverage.md`.

---

## 6. Recording a cassette

The integration suite proves the client works *today*. A cassette freezes that proof so the unit
suite can keep asserting against genuine Cloudflare payloads — offline, on every pull request, with
no credentials and no quota. `tests/cassettes/README.md` has the full format; the short version:

```bash
export CLOUDFLARE_IT_CASSETTE=readonly-smoke   # file: tests/cassettes/readonly-smoke.json
export CLOUDFLARE_IT_RECORD=true               # implied by the name
export CLOUDFLARE_IT_ENABLE_WRITES=true        # only if writes belong in the recording

dotnet test tests/CF.Net.IntegrationTests --filter "Category=Integration"
```

The recorder is `CFNet.TestKit.CassetteHandler`, a `DelegatingHandler` dropped into the
`IHttpClientFactory` pipeline. It is shared across every test in the run, so one file collects the
whole transcript — and a replay consumes a recording in the order it was made — and it merges into an
existing cassette rather than truncating it. Sharing a `DelegatingHandler` between factory pipelines
is not allowed (its `InnerHandler` can only be assigned once), so each client is handed a
`CassetteForwarder` around the one shared recorder.

Two things it guarantees:

* **Credentials never reach disk.** `Authorization`, `X-Auth-Key`, `X-Auth-Email`, `X-Api-Key`,
  `Cookie` and `Set-Cookie` are replaced with `<redacted>` on the way in, and
  `CassetteTests.Committed_cassettes_bind_to_the_typed_models` fails the build if a committed
  cassette carries a real value for any of them.
* **Replay never reaches the network.** The replaying handler's inner handler throws, so a request
  the cassette does not contain is a `CassetteMissException`, not a silent API call.

Everything *else* in a cassette is real data: account ids, zone names, KV values, DNS records. Read
the diff before committing, and do not record from an account you would not publish.

In GitHub Actions, run the **Integration** workflow manually and fill in the `cassette` input. The
file is attached as the `cassette-<name>` artifact; with `commit_cassette` ticked, the workflow
commits it back to the branch it ran on.

### Replaying a cassette offline

Once a cassette is committed, the **integration suite itself** can run against it — no credentials, no
network, no quota:

```bash
CLOUDFLARE_IT_CASSETTE=readonly-smoke CLOUDFLARE_IT_RECORD=replay \
  dotnet test tests/CF.Net.IntegrationTests \
  --filter "Category=Integration&ClassName~ReadOnly" --logger "console;verbosity=detailed"
```

Three things make that work:

* **The token is invented.** A replay authenticates against nothing, so the fixture substitutes a
  placeholder for `CLOUDFLARE_API_TOKEN`. Every credential header was redacted on the way in anyway.
* **The account and zone ids are recovered from the recording.** They are part of the URLs the match
  key is built from, so they cannot be invented — but they are not secrets either, and
  `CFNet.TestKit.CassetteContext` reads them straight back out of the recorded paths. Export them
  yourself and your values win.
* **Write lifecycles stay out.** `WriteIntegrationTests` generates a fresh resource name on every run
  and asserts that the response echoes it, which a recording cannot do. A replay therefore ignores
  `CLOUDFLARE_IT_ENABLE_WRITES`, and those tests skip with that explanation.

`.github/workflows/ci.yml` does exactly this for **every** cassette in `tests/cassettes/`, on every
push and pull request, in a step called *Replay committed cassettes (offline)*. That is the strongest
form of the guarantee this repository can give: real service code, real URLs, real Cloudflare
payloads, verified in CI without a single secret. A `CassetteMissException` there means the client now
asks Cloudflare something the recording never asked — drift you want to hear about, fixed by
re-recording.

---

## 7. Pointing the tests somewhere else

`CloudflareClientOptions.BaseAddress` is honoured by everything, and
`CLOUDFLARE_BASE_ADDRESS` feeds it. That gives you three useful configurations:

```bash
# Record what the integration suite really sends, with a proxy instead of the built-in recorder.
export CLOUDFLARE_BASE_ADDRESS='https://api.cloudflare.com/client/v4/'
export HTTPS_PROXY='http://127.0.0.1:8080'

# Talk to a local stub that replays recorded envelopes.
export CLOUDFLARE_BASE_ADDRESS='http://127.0.0.1:5080/client/v4/'
export CLOUDFLARE_API_TOKEN='not-a-real-token'

# Use a staging account.
export CLOUDFLARE_ACCOUNT_ID='<staging account>'
```

A staging account with one throwaway zone is the best setup for regular runs: keep the production
credentials out of CI entirely.

---

## 8. Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| Every test prints `SKIPPED: no Cloudflare credentials found` | the secrets are not reaching the process | secrets must be job-level `env:`, not `with:` on a step; `workflow_dispatch` from a fork has no secrets |
| The job dies at `Verify the credentials against the live API` | Cloudflare itself rejected the secret | the step prints Cloudflare's error verbatim; fix the secret — this check exists so you do not have to read 25 test failures to find that out |
| `9109 Authentication error [code 10000]` | token revoked, expired, or missing a permission | recreate the token; check the permission list above |
| `9109` on one product only | the token lacks that specific permission | add it — permissions are per product area |
| An **account-scoped** IP Access rule call answers 403 `[10000]` while the zone-scoped one works | the two paths do not share a permission requirement | add **Account Firewall Access Rules** Read and Write; Firewall Services covers the filters only |
| The filters probe answers `410 [10020] filters.api.deprecated` | Cloudflare has retired the filters API for this account | nothing — use `client.Rulesets`; the reach matrix files a 410 as a retirement, not as a missing permission |
| `10000` with a valid token, from CI only | client IP filtering on the token | remove the IP restriction for the CI token |
| `7403 D1_DATABASE_NOT_FOUND` | wrong account id | `CLOUDFLARE_ACCOUNT_ID` must own the database |
| `10007` on a zone you can see in the dashboard | the token's *Zone resources* scope excludes it | include the specific zone, or all zones |
| Writes skip even though credentials are set | `CLOUDFLARE_IT_ENABLE_WRITES` is not exactly `true` | tick the workflow input, or export `true` |
| The summary's skip table lists tests you expected to run | the secret or switch named in the *Why* column is missing | add it; the reason is the exact variable the test looked for |
| No cassette file appears after a recording run | `CLOUDFLARE_IT_CASSETTE` is empty, or every test skipped | set the name, and check the credentials are actually present |
| `CassetteMissException` during replay | the code asked Cloudflare something the recording never asked | re-record, or fix the call — this is the drift the cassette exists to catch |
| Rate limited (429) after several runs | purge or list quotas | wait; the transport already honours `Retry-After` when retries are on |

When a test fails, the log line includes the `CF-RAY`. Quoting it to Cloudflare support gets you the
server-side view of the request immediately.
