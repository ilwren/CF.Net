# Integration testing against the real Cloudflare API

CF.Net separates two kinds of tests, and the difference matters:

| | `tests/CF.Net.UnitTests` | `tests/CF.Net.IntegrationTests` |
| --- | --- | --- |
| Network | **never** — a stubbed `HttpMessageHandler` | real `api.cloudflare.com` |
| Credentials | none | `CLOUDFLARE_*` environment variables |
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

These are the same variables `CloudflareClientOptions.FromEnvironment()` reads, and the same ones
`.github/workflows/integration.yml` injects — so there is no separate CI-only configuration path.

When a variable is missing the test **does not fail**. xunit v2 has no dynamic skip, so the fixture
prints `SKIPPED: <reason>` through `ITestOutputHelper` and returns early. A run with no credentials
at all is a fast, green no-op — which is exactly what you want on a pull request from a fork.

The client itself is built through `AddCloudflareFromEnvironment()`, so the integration suite also
proves the dependency-injection registration works against the live API.

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

Prefer not to put a token in your shell history? Use
[`dotnet user-secrets`](https://learn.microsoft.com/aspnet/core/security/app-secrets) in a scratch
project and export from there, or a `.env` file that is already gitignored:

```bash
set -a; . ./.env; set +a
dotnet test tests/CF.Net.IntegrationTests --filter "Category=Integration"
```

`.gitignore` in this repository excludes `.env`, `.env.*` and `secrets.json`.

### Narrowing a run

```bash
# One test
dotnet test tests/CF.Net.IntegrationTests --filter "FullyQualifiedName~A_txt_record_can_be_created"

# Everything read-only
dotnet test tests/CF.Net.IntegrationTests --filter "Category=Integration&ClassName~ReadOnly"
```

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
```

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
*Allow tests that create and delete resources* → **Run workflow**.

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
or with `trx2junit`).

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
| `D1_databases_can_be_listed_and_queried` | `/d1/database`, plus `SELECT 1` and `FirstColumn<int>` |
| `Kv_namespaces_can_be_listed` | `/storage/kv/namespaces` |
| `R2_buckets_can_be_listed` | `/r2/buckets`, including the `{buckets:[…]}` unwrap |
| `Every_product_area_is_reachable_through_the_client_facade` | all nine services wired to one transport |
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

Resource names are generated with a timestamp and a random suffix (`cfnet-it-20260101120000-a1b2c3d4`)
so concurrent runs never collide, and a failed cleanup leaves something obviously disposable.

**Costs to be aware of.** All of these sit inside Cloudflare's free tier for a small number of runs,
but a Worker script counts towards your script limit, a D1 database towards your database limit, an
R2 bucket towards your bucket limit, and a cache purge is rate limited (30 per 15 minutes for
`purge_everything`, 100 per 15 minutes otherwise). Run the write suite deliberately, not on a
schedule.

---

## 6. Pointing the tests somewhere else

`CloudflareClientOptions.BaseAddress` is honoured by everything, and
`CLOUDFLARE_BASE_ADDRESS` feeds it. That gives you three useful configurations:

```bash
# Record what the integration suite really sends, with mitmproxy.
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

## 7. Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| Every test prints `SKIPPED: no Cloudflare credentials found` | the secrets are not reaching the process | secrets must be job-level `env:`, not `with:` on a step; `workflow_dispatch` from a fork has no secrets |
| `9109 Authentication error [code 10000]` | token revoked, expired, or missing a permission | recreate the token; check the permission list above |
| `9109` on one product only | the token lacks that specific permission | add it — permissions are per product area |
| `10000` with a valid token, from CI only | client IP filtering on the token | remove the IP restriction for the CI token |
| `7403 D1_DATABASE_NOT_FOUND` | wrong account id | `CLOUDFLARE_ACCOUNT_ID` must own the database |
| `10007` on a zone you can see in the dashboard | the token's *Zone resources* scope excludes it | include the specific zone, or all zones |
| Writes skip even though credentials are set | `CLOUDFLARE_IT_ENABLE_WRITES` is not exactly `true` | tick the workflow input, or export `true` |
| Rate limited (429) after several runs | purge or list quotas | wait; the transport already honours `Retry-After` when retries are on |

When a test fails, the log line includes the `CF-RAY`. Quoting it to Cloudflare support gets you the
server-side view of the request immediately.
