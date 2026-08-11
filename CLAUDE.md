# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows desktop widget (WPF, planned) that shows live quota/usage for locally installed AI coding tools — Claude Code and Codex. `docs/PRD.md` is the authoritative spec; read it before non-trivial work.

The repo is currently at the **verification spike** stage: `src/AiUsageMonitor.Poc` is a console harness that proves the provider retrieval mechanisms actually work. The WPF app does not exist yet.

## Commands

```powershell
dotnet build                                   # warnings are errors — must be clean
dotnet test                                    # domain unit tests
dotnet run --project src/AiUsageMonitor.Poc    # runs all provider probes, prints a report
powershell -File build/publish.ps1             # single self-contained .exe (~65 MB)
```

Domain tests live in `tests/AiUsageMonitor.Domain.Tests`. For a single test, prefer `dotnet test --filter FullyQualifiedName~<Name>`.

Regenerating the Codex protocol schema (the source of truth for its wire format):

```powershell
codex app-server generate-json-schema --out <dir> --experimental
codex app-server generate-ts --out <dir>    # TS bindings name the methods far more clearly
```

## Provider mechanisms (empirically verified — do not re-derive)

Verified against Claude Code **2.1.226** and codex-cli **0.144.6** on Windows 11.

| Provider | Mechanism | Tier | Update model |
|---|---|---|---|
| Codex | `codex app-server` → JSON-RPC `account/rateLimits/read` | Official | Pull (poll) |
| Claude Code | `GET api.anthropic.com/api/oauth/usage` + local OAuth token | **Unofficial** | Pull (poll) |

### Codex — JSON-RPC over stdio

Launch the **vendored exe directly**, never the npm shim (`codex.cmd`/`.ps1` just re-exec it through node):
`%APPDATA%\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-*\vendor\*\bin\codex.exe app-server`

Framing is **newline-delimited JSON**, UTF-8, LF only, no BOM, no `Content-Length` headers. Send `initialize` (mandatory — otherwise `-32600 Not initialized`) then `account/rateLimits/read` (takes no `params`). Pipelining both writes before reading is safe. The `initialized` notification is not required.

Non-obvious behaviours that break naive clients:

- **Responses omit `"jsonrpc":"2.0"`.** Frames are `{"id":2,"result":{…}}`. A deserializer requiring `jsonrpc` throws on every response.
- **Unsolicited notifications interleave.** `remoteControl/status/changed` arrives before your `initialize` result. Any line without an `id` is a notification — skip it and keep reading. Never assume the first line back is your answer.
- Cold start ~1.7 s, warm ~55 ms; the rate-limit call adds ~500 ms (it hits the network). Closing stdin exits cleanly with code 0.
- Prefer `result.rateLimitsByLimitId` (a map, iterate all entries) over `result.rateLimits`, which is documented as the backward-compatible single-bucket view.
- `accountRateLimitsUpdated` notifications only fire during an active model turn, so they are useless to an observer app — poll instead. When they do arrive they are **sparse rolling updates** that must be merged into the last full snapshot, never treated as a replacement.

### Claude Code

The only mechanism this app uses: read `claudeAiOauth.accessToken` from the local credential store and call the provider's own first-party usage endpoint (`anthropic-beta: oauth-2025-04-20`). This is **undocumented and may break without notice** — it must fail into an explicit error state, never fabricate data, and always be labelled as unofficial in UI and diagnostics. There is no official fallback: if this endpoint fails, the provider goes to `Unsupported`/`Error`, full stop — never a silently degraded or fabricated value.

**statusLine was evaluated and rejected — do not re-add it as a fallback.** The statusLine JSON contract (`rate_limits` piped on stdin) was investigated and proven parseable, then rejected as a product mechanism because it is push-only (fires only inside an interactive session, never under `-p`), requires a user-approved modification of the user's existing `~/.claude/settings.json` statusLine configuration to tee the data out, and produces data that is stale whenever no session is running — the common case for a persistent desktop widget. The recorded sample in `fixtures/claude-statusline-sample.json` is kept solely as regression coverage for the duck-typed extractor's `used_percentage` dialect, asserted in `DuckTypedQuotaExtractorTests`, not as evidence the mechanism is supported.

Investigated and confirmed to carry **no** quota data: hook payloads, OpenTelemetry metrics, all non-interactive CLI output (`-p --output-format json` has token counts and cost only), and `~/.claude/stats-cache.json`. Add statusLine to this "carries no usable signal for this app" list too — not because it lacks quota data (it has some), but because it cannot be relied upon as described above.

## Architecture rules

Layering follows PRD §21: `Domain/` is provider-neutral (models, states, extraction); `Providers/` holds adapters behind `IProviderProbe`. Provider-specific semantics must not leak into shared or UI code.

**The domain model must stay generic.** No property may be named after a plan period — no `FiveHourQuota`, no `WeeklyQuota`. Quota windows are *discovered*, and their count, names, and durations are never assumed. Codex already returns `secondary: null` and a nullable `windowDurationMins`, so a fixed two-window shape is wrong today, not just in theory.

`Domain/DuckTypedQuotaExtractor.cs` is the reason new provider windows need no code change: it walks arbitrary JSON and treats any object carrying **both** a percent-ish key and a reset-ish key as a quota window. This is what lets one parser handle both Claude dialects (`used_percentage` + unix seconds from statusLine; `utilization` + ISO-8601 from the usage endpoint).

Two traps it must keep guarding:

- `context_window.used_percentage` is context fill, **not** subscription quota. It is correctly excluded only because it lacks a reset key. Keep that assertion.
- Unrecognised name tokens must be preserved verbatim, never dropped or reinterpreted. A provider inventing `three_hour_nimbus` must render with its own label.

Missing data is `null` and surfaces as `Waiting`/`Unavailable` — **never** as `0`. A window duration is inferred only when it can be derived honestly; otherwise the elapsed-time marker (PRD §16) is omitted.

## Hard constraints

Per PRD §4.1.1 and §23 — these are product requirements, not style preferences:

- Credentials are used **in-memory only**, only against that provider's own first-party host over TLS. Never log, persist, cache, display, or copy a token; never put one in `Extra`, an exception message, or a diagnostic dump. This app never refreshes or rewrites a credential — token lifecycle stays the provider's job.
- No website scraping, browser automation, cookie or browser-profile access, telemetry, analytics, or third-party transmission.
- Never modify provider configuration without explicit user approval, a preview, a backup, and a restore path (PRD §11). Currently moot in practice — the app's one Claude Code mechanism reads a credential file and calls an HTTP endpoint; it does not touch `~/.claude/settings.json` or any other provider configuration — but the constraint stands should config modification ever become necessary.
- No administrator privileges.
- Every mechanism carries a visible tier (Official/Unofficial). A value obtained unofficially must never be presented as official.

## Conventions

Windows-only. The primary shell is PowerShell (5.1 — no `&&`, no ternary); a Bash tool is also available and takes POSIX syntax. Use `System.Text.Json`; keep dependencies minimal and justified. Provider operations are async, cancellable, and timeout-bounded. One provider failing must never affect the other or crash the process.
