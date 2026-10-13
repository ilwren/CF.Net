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

| Permission | Access |
| --- | --- |
| User | API Tokens — Read |
| Zone | Zone — Read |
| Zone | DNS — Edit |
| Zone | Zone Settings — Edit *(cache purge)* |
| Account | Account Settings — Read |
| Account | Workers Scripts — Edit |
| Account | Pages — Edit |
| Account | D1 — Edit |
| Account | Workers KV Storage — Edit |
| Account | Workers R2 Storage — Edit |

**Client IP address filtering**: leave it empty for CI. GitHub-hosted runners use a large, changing
pool of egress addresses, and an IP allowlist will make every run fail with a 403 that looks like a
bad token.

**TTL**: set one. A CI token that never expires is a standing risk.

For read-only runs you can drop every `Edit` permission — the `ReadOnlyIntegrationTests` class will
still exercise everything it can.

**Resources are as important as permissions.** Under *Account Resources* include the account you are
testing; under *Zone Resources* include that one zone (or *All zones*). A token with the right
permissions but no zone resource returns `10007 Route not found` for a zone you can see perfectly well
in the dashboard, which reads like a bug in the client and is not.

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
| `Every_product_area_is_reachable_through_the_client_facade` | all twelve services wired to one transport |
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
