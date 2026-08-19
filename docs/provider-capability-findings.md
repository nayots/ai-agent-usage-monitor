# Provider Capability Findings

Verification record for PRD §26 step 2 ("record provider capability findings, schemas, limitations, and version behavior in repository documentation").

| | |
|---|---|
| Date of verification | 2026-08-10 |
| Platform | Windows 11 Pro 10.0.26200, .NET SDK 10.0.301 |
| Claude Code | 2.1.226 |
| Codex CLI | 0.144.6 |
| Verified by | `src/AiUsageMonitor.Poc` console harness, against live accounts |

All findings below were obtained empirically by running the mechanisms, not from documentation alone.

---

## 1. Summary

| Provider | Mechanism | Tier | Update model | Result |
|---|---|---|---|---|
| Codex | `codex app-server` → JSON-RPC `account/rateLimits/read` | Official | Pull (poll) | Verified, live data |
| Claude Code | `GET api.anthropic.com/api/oauth/usage` + local OAuth token | Unofficial | Pull (poll) | Verified, live data |

**Decision:** each provider has exactly one supported mechanism. Claude Code's statusLine contract was evaluated and rejected (§4 below).

---

## 2. Codex

### Mechanism

`codex app-server` exposes a JSON-RPC server over stdio. It also ships a machine-readable protocol description, which should be treated as the source of truth in preference to anything hardcoded:

```
codex app-server generate-json-schema --out <dir> --experimental
codex app-server generate-ts --out <dir>
```

The TypeScript bindings name methods far more legibly than the JSON schemas; `ClientRequest.ts` lists all 120 methods.

### Executable

Launch the vendored executable directly. The npm shims (`codex.cmd`, `codex.ps1`, `codex`) only re-exec it through node, and shelling out via PowerShell is unnecessary:

```
%APPDATA%\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe -s read-only -a untrusted app-server
```

Discovery should glob `codex-win32-*\vendor\*\bin\codex.exe` to stay architecture-agnostic, and fall back to `codex.cmd` on PATH.

`-s read-only -a untrusted` are **flags of the top-level `codex` command, not of `app-server`**, so they must precede the subcommand. Verified against 0.144.6 (2026-08-19): accepted, and the `account/rateLimits/read` response is byte-identical to the unflagged call. They are defence-in-depth only — this application never opens a session, so nothing it does today is affected by them; they exist so that the process it spawned is already capped if a future app-server ever acts on its own.

### Wire protocol

Newline-delimited JSON (JSONL). UTF-8, LF only, no BOM, no `Content-Length` headers. Send both lines and then read; pipelining is safe.

```
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"ai-agent-usage-monitor","title":null,"version":"0.1.0"}}}
{"jsonrpc":"2.0","id":2,"method":"account/rateLimits/read"}
```

`initialize` is mandatory — omitting it returns `{"error":{"code":-32600,"message":"Not initialized"}}`. The `initialized` notification is **not** required. `account/rateLimits/read` takes no `params` key at all.

### Response shape

Observed live (`RateLimitSnapshot`):

| Field | Type | Notes |
|---|---|---|
| `usedPercent` | int | The **only** required field in a window |
| `resetsAt` | int, nullable | Unix **seconds** |
| `windowDurationMins` | int, nullable | Enables the elapsed-time marker (PRD §16) |
| `limitId` / `limitName` | string, nullable | Provider-supplied identity and label |
| `planType` | enum | Includes an `unknown` member |
| `primary` / `secondary` | window, nullable | `secondary` was **null** on the verified account |
| `rateLimitsByLimitId` | map | Multi-bucket view; prefer this |
| `rateLimits` | object | Documented as the backward-compatible single-bucket view |
| `credits`, `individualLimit`, `rateLimitReachedType` | nullable | |
| `rateLimitResetCredits` | nullable | **Result-level**, a sibling of the buckets — not inside one |

Snapshot observed 2026-08-10: plan `plus`, `limitId: codex`, one bucket, `primary` at 100% used, `windowDurationMins: 10080` (7 days), `secondary: null`, `rateLimitReachedType: rate_limit_reached`.

### Generated shapes (the source of truth)

Do not infer these from third-party documentation — see the trap below. Regenerate with:

```powershell
codex app-server generate-ts --out <dir>
codex app-server generate-json-schema --out <dir> --experimental
```

As of 0.144.6:

```ts
GetAccountRateLimitsResponse = { rateLimits, rateLimitsByLimitId, rateLimitResetCredits }
SpendControlLimitSnapshot    = { limit: string, used: string, remainingPercent: i32, resetsAt: i64 }  // all four REQUIRED
RateLimitResetCreditsSummary = { availableCount: i64, credits: RateLimitResetCredit[] | null }
RateLimitResetCredit         = { id, resetType, status, grantedAt, expiresAt?, title?, description? }
RateLimitResetCreditStatus   = "available" | "redeeming" | "redeemed" | "unknown"
PlanType = "free" | "go" | "plus" | "pro" | "prolite" | "team"
         | "self_serve_business_usage_based" | "business"
         | "enterprise_cbp_usage_based" | "enterprise" | "edu" | "unknown"
```

**`individualLimit` reports `remainingPercent`, not a used percentage.** OpenUsage's public provider
docs describe this object as carrying `limit`/`used`/`resetsAt` and do not mention `remainingPercent`
at all; a client that trusts that description and maps the percentage straight through renders a
spend limit 5% consumed as 95% consumed. `UsedPercent = 100 - remainingPercent`. It is mapped as a
quota window with **no** `WindowDuration` — the payload carries a reset instant and nothing stating
how long the period runs, so inferring "monthly" from the reset boundary would be a guess and the
§16 elapsed marker is omitted instead.

`individualLimit` is `null` on `plus`; the plan types that populate it are the business and
enterprise seats the `PlanType` enum lists.

**`rateLimitResetCredits.credits`: `null` and `[]` are different facts.** The schema's own doc
comment says so — `null` means only `availableCount` is known, `[]` means detail rows were fetched
and none came back. The array may also be capped shorter than `availableCount`, so its length is
never a recount. These are credits that reset a rate limit early, so they are deliberately **not**
a quota window; they ride in `Extra` as `resetCredits.availableCount` and, only when the array is
genuinely present, `resetCredits.detailRows`.

### Behaviours that break naive clients

1. **Responses omit `"jsonrpc":"2.0"`.** Real frames are `{"id":2,"result":{…}}`. A deserializer that requires a `jsonrpc` property throws on every response.
2. **Unsolicited notifications interleave.** `remoteControl/status/changed` arrives *before* the `initialize` result. Any line without an `id` is a notification — skip it and keep reading. Never assume the first line after a write is the answer.
3. **`accountRateLimitsUpdated` is not usable as a push source.** It was verified not to arrive during a 15-second idle window; it fires only while a model turn is running, which an observer application must never start. Poll instead. When such a notification *does* arrive, its schema states it is a **sparse rolling update** that must be merged into the last full snapshot, never treated as a replacement.

### Timing and lifecycle

Cold start ~1.7 s; warm ~55–70 ms. `account/rateLimits/read` adds ~450–500 ms because it hits the network — the value is live, not cached. Budget ~2.3 s worst case. Closing stdin exits the process cleanly with code 0 in ~10–25 ms. stdout was valid JSON throughout and stderr was empty, but a client should still skip unparseable lines defensively: `configWarning`, `warning` and `deprecationNotice` notification methods exist in the protocol.

---

## 3. Claude Code — adopted mechanism

### Mechanism

```
GET https://api.anthropic.com/api/oauth/usage
Authorization: Bearer <claudeAiOauth.accessToken from the local credential store>
anthropic-beta: oauth-2025-04-20
```

The token is the one Claude Code itself writes on login. This is an **undocumented, provider-owned endpoint**, adopted under PRD §4.1.1 because no official mechanism can deliver equivalent capability. It carries no stability guarantee and may change without notice.

Observed latency: ~350–560 ms.

Independently corroborated on 2026-08-19: OpenUsage reaches the same endpoint with the same local
OAuth token. Notably it treats this as its *fallback* — its primary Claude source is
`claude.ai/api/organizations/{org_uuid}/usage` authenticated with **imported browser session
cookies**, which PRD §4.1.1/§23 forbids outright (no cookie or browser-profile access) and which is
macOS-only in any case. This application uses their plan B as its plan A, by design.

### Other fields in the credential file

`claudeAiOauth` carries more than the token, and none of the rest is secret:

| Field | Type | Use |
|---|---|---|
| `expiresAt` | unix **ms** | Access-token expiry |
| `refreshTokenExpiresAt` | unix **ms** | Whether Claude Code can repair the sign-in unaided |
| `subscriptionType` | string | Plan label (`pro` observed) — the Claude counterpart to Codex `planType` |
| `rateLimitTier` | string | Tier label (`default_claude_ai` observed) |

The two expiries let an expired sign-in be recognised **before** a request is spent learning it from
a 401 — which matters because every call counts toward the throttling that put a 120-second floor on
this provider. The check is deliberately one-directional: it may only skip a call it is *confident*
would fail, so a missing or implausible timestamp (outside 2000–2100 read as ms) falls through to
attempting the request exactly as before. A bug there must never be able to disable a working widget.

`refreshTokenExpiresAt` is what separates the two failure messages: while it is live, running any
Claude Code session repairs things; once it is gone, only a fresh sign-in will.

### Response shape

Uses `utilization` (0–100) plus **ISO-8601** `resets_at` — note this differs from the statusLine contract, which used `used_percentage` plus unix seconds. Window identity is the **key name**; there is no label field.

Windows observed live on 2026-08-10: `five_hour`, `seven_day`, and `nimbus_quill`.

### Finding: an undocumented quota window exists in production

The live response contained a window keyed `nimbus_quill` — a name that appears in no public documentation, alongside the expected `five_hour` and `seven_day`. It returned a `utilization` value but no usable reset time.

This is the single most important finding of the verification exercise, because it converts PRD §8/§13 from a design preference into a demonstrated requirement:

- An implementation with hardcoded `FiveHourQuota` / `WeeklyQuota` properties would have **silently dropped a live quota window**, leaving the user unaware a limit existed.
- An implementation validating against an allowlist or enum of known window names would have dropped it or thrown.

The harness rendered it correctly: provider-supplied name preserved verbatim, marked partial, with the countdown and elapsed-time marker omitted rather than fabricated.

Fields documented elsewhere (`seven_day_opus`, `seven_day_sonnet`, `extra_usage`, `limits[]`) were **not** present on the verified account. The window set must therefore be treated as varying by account, plan, and time — never assumed.

### Consequence of having no official fallback

statusLine was rejected (§4), so Claude Code has no second mechanism. If the endpoint stops working, the provider must enter an explicit `Unsupported` or `Error` state per PRD §10. It must never fabricate, infer, estimate, or fall back to locally computed token counts.

---

## 4. Claude Code — mechanisms evaluated and rejected

| Avenue | Quota windows? | Why rejected |
|---|---|---|
| **statusLine JSON contract** | **Yes** — `rate_limits.five_hour`, `.seven_day` | **Push-only.** Never fires under `-p`; only inside an interactive session. Cannot be polled, so data is stale whenever no session runs. Requires modifying the user's existing statusLine configuration. Also absent until the first API response of a session, and absent entirely for API-key/Bedrock/Vertex auth. |
| Hook payloads | No | SessionStart/UserPromptSubmit/Stop/SessionEnd carry session and prompt metadata only. No usage or quota keys. |
| OpenTelemetry export | No | 8 metrics / 15 events, all consumption-oriented (`claude_code.token.usage`, `.cost.usage`). No quota, reset, or percentage metric. Local receipt via OTLP is feasible but carries no window data. |
| Non-interactive CLI | No | No `usage` subcommand. `-p --output-format json` returns token counts and cost only. `auth status` gives `subscriptionType` but no quota. `/usage` is an interactive TUI panel. |
| `~/.claude/stats-cache.json` | No | Undocumented internal file; lifetime token/activity aggregates only, no quota windows, and observed stale (last written February 2026). |

statusLine may be reconsidered if the adopted endpoint ceases to function. The recorded sample is retained in `fixtures/` as parser test data only.

---

## 5. Design implications

1. **The generic model is validated.** Two providers, three dialects: `usedPercent` + unix seconds (Codex), `utilization` + ISO-8601 (Claude OAuth), `used_percentage` + unix seconds (statusLine). One duck-typed extractor handles all three — any object carrying both a percent-ish key and a reset-ish key is a quota window.
2. **`context_window.used_percentage` is a false-positive trap.** It looks like a quota but is context fill. It is excluded only because it has no reset field. This must remain an explicit regression assertion.
3. **Nullability is the norm, not the exception.** `secondary`, `resetsAt`, `windowDurationMins`, `limitName` are all nullable and were observed null in practice. Missing data must surface as unknown, never as zero.
4. **The elapsed-time marker needs a verified window duration.** Codex supplies `windowDurationMins` explicitly. The Claude endpoint does not, so duration is inferred from the window name (`five_hour` → 5h) and tagged `duration_source=inferred_from_name`. When the name does not parse — as with `nimbus_quill` — the duration stays null and the marker is omitted per §16.
5. **Both providers are poll-based.** Neither offers usable push. Refresh scheduling can be uniform.
6. **Elapsed-vs-used comparison has real diagnostic value.** The verified Codex account was at 100% used with only ~24% of the window elapsed — information a plain progress bar cannot convey.

## 6. Security posture

The OAuth access token is read into a local variable, used once to build an `Authorization` header, and discarded. It is never logged, persisted, cached, copied, displayed, or placed in diagnostics; exception handlers record only exception type names so a message can never carry the token or URL. The destination host is hardcoded and automatic redirects are disabled, so no other host is reachable. The application never refreshes, rewrites, or invalidates the credential — token lifecycle remains the provider's responsibility. No provider configuration was modified during verification.

## 7. Not yet verified

- Codex `secondary` windows and multi-entry `rateLimitsByLimitId` maps — code paths implemented but unexercised, as the verified account has a single bucket with `secondary: null`.
- Behaviour when the unofficial endpoint changes shape or is withdrawn.
- Behaviour on Claude Code API-key, Bedrock, or Vertex authentication.
- Non-`plus` Codex plan types. `individualLimit` is now **implemented against the generated schema
  and unit-tested, but never yet observed populated** — it is null on `plus`. The first business or
  enterprise seat to run this is the real test of that mapping, and the inversion
  (`100 - remainingPercent`) is the thing to check first if a spend limit ever renders backwards.
- Codex reset credits beyond `availableCount`: the live account returned `credits: []`, so the
  detail rows (`id`, `status`, `grantedAt`, `expiresAt`, `title`, `description`) have never been seen
  populated and only their count is surfaced today.
