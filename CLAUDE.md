# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows desktop widget (WPF) that shows live quota/usage for locally installed AI coding tools — Claude Code and Codex. `docs/PRD.md` is the authoritative spec; read it before non-trivial work.

The repo has three projects today. `src/AiUsageMonitor.Domain` is a provider-neutral `net10.0` class library (models, states, extraction) with zero `PackageReference`, backed by `tests/AiUsageMonitor.Domain.Tests` (xUnit, 70 tests). `src/AiUsageMonitor.App` is the WPF shell — it exists and builds, but is **deliberately empty** pending the design brief (`docs/design/design-prompt.md`); do not add UI to it without one. `src/AiUsageMonitor.Poc` is retained as the live console harness that proves the provider retrieval mechanisms actually work against real installs — it is not superseded by the other two projects.

## Commands

```powershell
dotnet build                                   # warnings are errors — must be clean
dotnet test                                    # domain unit tests
dotnet run --project src/AiUsageMonitor.Poc    # runs all provider probes, prints a report
powershell -File build/publish.ps1             # single self-contained .exe (~65 MB)
```

Cutting a release (the tag is the trigger — everything else is automatic):

```powershell
# 1. Bump <VersionPrefix> in Directory.Build.props and commit it.
# 2. Tag that commit and push the tag:
git tag v0.1.0
git push origin v0.1.0
```

`.github/workflows/release.yml` verifies the tag against `<VersionPrefix>` **before** it
builds, runs the tests, publishes the self-contained `.exe`, and attaches **three** assets
to a GitHub release: the `.exe`, its `SHA256`, and a `.zip` holding both. A tag that
disagrees with the declared version fails in seconds. The artifact is always the
self-contained build — never the framework-dependent one, which needs the .NET 10 Desktop
Runtime preinstalled and so fails on exactly the machines this application must work on.

The zip exists because managed Windows machines routinely sit behind a filter that refuses
a bare `.exe` download; it ships *alongside* the `.exe`, never instead of it. There is
deliberately no `.zip.sha256` — zip entries carry their own CRC32, so corruption is caught
on extraction, and the checksum worth publishing is the one on the binary that actually
runs, which travels inside the archive. Note that a release's zip can only ever be built by
the release run itself: two publishes of the same commit do not produce byte-identical
executables, so a hand-built zip would contain a different binary from the `.exe` beside it.

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

**statusLine was evaluated and rejected — do not re-add it as a fallback.** The statusLine JSON contract (`rate_limits` piped on stdin) was investigated and proven parseable, then rejected as a product mechanism because it is push-only (fires only inside an interactive session, never under `-p`), requires a user-approved modification of the user's existing `~/.claude/settings.json` statusLine configuration to tee the data out, and produces data that is stale whenever no session is running — the common case for a persistent desktop widget. The recorded sample in `fixtures/claude-statusline-sample.json` is kept solely as regression coverage for the duck-typed extractor's `used_percentage` dialect, asserted in `DuckTypedQuotaExtractorTests`, not as evidence the mechanism is supported. `fixtures/claude-usage-limits-sample.json` is a synthetic-only usage-endpoint shape fixture for Claude adapter normalization tests; its percentages and reset instants are not captured account data.

Investigated and confirmed to carry **no** quota data: hook payloads, OpenTelemetry metrics, all non-interactive CLI output (`-p --output-format json` has token counts and cost only), and `~/.claude/stats-cache.json`. Add statusLine to this "carries no usable signal for this app" list too — not because it lacks quota data (it has some), but because it cannot be relied upon as described above.

## Architecture rules

Layering follows PRD §21: `Domain/` (i.e. the `src/AiUsageMonitor.Domain` class library) is provider-neutral (models, states, extraction); `Providers/` is a folder inside `src/AiUsageMonitor.Poc` holding adapters behind `IProviderProbe`. These are **not** peer folders under one project — `Domain/` is its own standalone project precisely so it can be referenced without depending on the POC or any provider adapter. Provider-specific semantics must not leak into shared or UI code.

**The domain model must stay generic.** No property may be named after a plan period — no `FiveHourQuota`, no `WeeklyQuota`. Quota windows are *discovered*, and their count, names, and durations are never assumed. Codex already returns `secondary: null` and a nullable `windowDurationMins`, so a fixed two-window shape is wrong today, not just in theory.

`Domain/DuckTypedQuotaExtractor.cs` is the reason new provider windows need no code change: it walks arbitrary JSON and treats any object carrying **both** a percent-ish key and a reset-ish key as a quota window. This is what lets one parser handle both Claude dialects (`used_percentage` + unix seconds from statusLine; `utilization` + ISO-8601 from the usage endpoint).

Two traps it must keep guarding:

- `context_window.used_percentage` is context fill, **not** subscription quota. It is correctly excluded only because it lacks a reset key. Keep that assertion.
- Unrecognised name tokens must be preserved verbatim, never dropped or reinterpreted. A provider inventing `three_hour_nimbus` must render with its own label.

Missing data is `null` and surfaces as `Waiting`/`Unavailable` — **never** as `0`. A window duration is inferred only when it can be derived honestly; otherwise the elapsed-time marker (PRD §16) is omitted.

### Request cadence and throttling (added 2026-08-17)

Implemented from `docs/specs/2026-08-14-provider-request-cadence-and-rate-limits.md`. The rules that are expensive to rediscover:

- **"Throttle" and "rate limit" are different things here.** A *rate limit* is a quota window — the thing the widget displays, and literally what Codex's `account/rateLimits/read` returns. A *throttle* is the provider refusing a request because we asked too often. Never use one word for the other; `Domain/ThrottleAdvice.cs` exists under that name for exactly this reason.
- **60 seconds is the effective polling floor**, global and per-provider (`AppSettings.MinimumRefreshSeconds`). A hand-edited sub-floor value resolves to 60 and is **never rewritten to disk** — same read-time-sanitize pattern as `EffectiveAlertThresholds`.
- **A 429 is not a new `ConnectionState`.** It is `Error` plus a `ThrottleAdvice` on the snapshot. A provider-named `Retry-After` instant is honoured exactly (bounded at 1 h against a malformed header); with no usable header the *scheduler* applies 2 → 4 → 8 minutes. There is deliberately **no 15-minute starting fallback**.
- **A throttle cooldown is the one gate a forced refresh may not bypass.** An ordinary failure backoff still yields to a manual retry — that is how a provider gets recovered by hand.
- **One in-flight request per provider**, shared rather than skipped, so a manual refresh cannot flick the spinner off while a probe is still running. Claude and Codex stay concurrent with each other; no lock is held across a provider await.
- **Reset alignment only ever moves a read earlier.** When a successful snapshot reports a
  future reset, `NextAttempt` may come forward to just after it (`ResetAlignmentBuffer`, 30 s),
  never below the 60-second floor and never past what the interval already chose — so at the
  default 60-second cadence it changes nothing, and it only earns its keep on a long interval.
  It is derived from each snapshot and never stored, so a revised reset instant needs no
  invalidation. Resets within one floor of each other collapse to a single read. **A reset at or
  before `now` is ignored** — aligning to a past instant re-schedules against that same instant
  forever, which is the one way this becomes the second polling loop it must never be.
- **Only `SessionLock`/`SessionUnlock` pause polling.** Never infer a lock from input idleness, a timer, or widget visibility. The manual triggers (`ManualGlobal`, `ManualCard`) are exempt from the pause — which is why a refresh's `RefreshTrigger` must match what actually caused it, not be hardcoded.

Claude's usage response also carries a `limits[]` array that, on the observed account, **restates the top-level windows exactly** rather than adding new ones. `Providers/Claude/ClaudeScopedLimits.cs` normalizes it and suppresses duplicates by *reset instant + percentage* — never by name, because the array says `session`/`weekly` where the top level says `five_hour`/`seven_day`. Do not "fix" this by adding `percent` to `DuckTypedQuotaExtractor.PercentKeys`: that renders every window twice. Evidence is in Appendix A of the spec.

## Hard constraints

Per PRD §4.1.1 and §23 — these are product requirements, not style preferences:

- Credentials are used **in-memory only**, only against that provider's own first-party host over TLS. Never log, persist, cache, display, or copy a token; never put one in `Extra`, an exception message, or a diagnostic dump. This app never refreshes or rewrites a credential — token lifecycle stays the provider's job.
- No website scraping, browser automation, cookie or browser-profile access, telemetry, analytics, or third-party transmission.
- Never modify provider configuration without explicit user approval, a preview, a backup, and a restore path (PRD §11). Currently moot in practice — the app's one Claude Code mechanism reads a credential file and calls an HTTP endpoint; it does not touch `~/.claude/settings.json` or any other provider configuration — but the constraint stands should config modification ever become necessary.
- No administrator privileges.
- Every mechanism carries a visible tier (Official/Unofficial). A value obtained unofficially must never be presented as official.
- **User- and machine-agnostic (added 2026-08-11).** Someone who is not the author, on a Windows machine that is not the author's, must be able to download the GitHub release artifact and run it. No hardcoded user paths — resolve per-user locations at runtime via `Environment.GetFolderPath`. The release artifact stays self-contained, never the framework-dependent build, which requires the .NET 10 Desktop Runtime preinstalled. Both providers must degrade to `NotInstalled` where absent, never crash.

## Conventions

Windows-only. The primary shell is PowerShell (5.1 — no `&&`, no ternary); a Bash tool is also available and takes POSIX syntax. Use `System.Text.Json`; keep dependencies minimal and justified. Provider operations are async, cancellable, and timeout-bounded. One provider failing must never affect the other or crash the process.

## Where process documents live

One location per kind, no parallel trees:

| Kind | Path |
|---|---|
| Implementation plans | `docs/plans/` |
| Design specs / brainstorm output | `docs/specs/` |

This **overrides the Superpowers defaults** (`docs/superpowers/plans/`, `docs/superpowers/specs/`) — those skills state that a user preference wins, and this is it. The plan path is not a preference at all but a requirement: the codex-workflow block below, and `docs/codex-workflow.md`, both name `docs/plans/<feature>.md` as the file a delegation is run against, so a plan written anywhere else is a plan Codex is never pointed at.

Do not recreate `docs/superpowers/`. If a skill writes there by default, move the file and say so.

<!-- codex-workflow:begin v7 -->
# Claude ↔ Codex workflow

- **Claude** (you): PLANNING and REVIEW. You decide whether, and how, to offload
  implementation to Codex.
- **Codex** (runs its own Superpowers): IMPLEMENTATION, when offloaded.

Codex bills my Codex/ChatGPT plan, not my Claude plan, so the two limits fail
independently — how much that actually saves depends on my subscriptions, so
don't state it as guaranteed. Offloading is OPTIONAL, decided per feature.

## Gate — read `docs/codex-workflow.md` before you delegate

**Before composing a delegation call, polling a job, or reviewing delegated
work, read `docs/codex-workflow.md`** — the procedure, plus Codex's own role.
Not "consult if unsure": read it. The companion's flag contract is pinned to one
plugin version and the polling loop has stop conditions that are wrong to guess
at. It must say `v7`, matching this block; if it is missing or differs, **say so
and stop rather than working from memory** — a stale procedure is worse than
none, being confidently wrong. Re-run `/codex-workflow-setup` to restore the pair.

## Rules that are already damage by the time you notice

Here, not in the reference, because reading them afterwards is too late.

- **Of the `/codex:*` commands you may invoke ONLY `/codex:rescue` and
  `/codex:setup`.** `status`, `result`, `review`, `adversarial-review`,
  `transfer` and `cancel` set `disable-model-invocation: true` — they are
  USER-invoked. Never say you'll poll, collect, or review with them; name the
  command and ask me to run it. (Plugin v1.0.6 — recheck on upgrade.)
- **Always pass a prompt with `--prompt-file`; never as a positional argument.**
  Positionals are concatenated into a shell command line without escaping:
  backtick-delimited text is **executed and replaced by its output**, silently
  deleting instructions, and newlines collapse to spaces.
- **Write prompt files OUTSIDE the repository**, and **never write into the
  working tree while a delegation is live.** Codex deletes untracked files that
  appear after it starts, treating them as its own tooling residue.
- **Never `--resume` after a dead or crashed worker.** The record permanently
  blocks resume for that workspace (`Task <id> is still running`) and no
  supported command clears it. Relaunch `--fresh`, restating the working-tree
  state in the prompt.
- **Never enable the review gate** (`/codex:setup --enable-review-gate`): in this
  workflow it can loop Claude↔Codex and drain usage limits. Reviews stay manual.
- **The sandbox patch is machine-wide.** A SessionStart hook rewrites the
  plugin's hardcoded sandbox to `danger-full-access`, giving Codex full
  filesystem and network access in *every* project on this machine, including
  ones that never opted in. Unpatched, Codex can neither commit nor reach the
  network. State with `node ~/.claude/scripts/codex-full-access-patch.mjs
  --check`; undo with `--revert`.
- **`/codex:setup` reporting `ready: true` certifies almost nothing.** It tests
  neither egress, nor `.git` writability, nor the resume path. Read it as "the
  CLI exists and is authenticated", nothing more.

## Step 1 — Offload decision (before implementation)

Offloading is a choice, not a default. Recommend one way or the other with a
one-line reason; never offload silently.

**Before anything else, check the first task for a package-manager or network
step.** If it runs `npm install`, `pip install`, `go get`, a registry fetch, an
API call or a container pull, either the sandbox patch must be active or you
install the dependencies yourself first and delegate the rest. Do not delegate a
task whose first action is an install and hope.

OFFLOAD when the work is a genuine multi-task feature (~3+ discrete tasks) and
the plan is self-contained enough for a fresh session.

DO NOT offload — do it yourself — when it's small (one file, a quick bugfix, a
config tweak), or when you foresee friction:
- Codex lacks access it needs (a credential, a private dependency, a service
  only wired into this environment).
- The change is format- or tooling-sensitive in a way Codex may not honour
  (formatter/linter config, generated files, codegen, pre-commit hooks, import
  ordering, encoding).
- The task needs live context from this session that won't travel in a plan file.
- Tight feedback loops where round-tripping is slower than doing it inline.

**Token overhead is not the deciding factor on the default path.** A direct
companion call costs a few hundred Claude-side tokens; the `/codex:rescue`
subagent costs ~20k regardless of task size. So the reason not to offload small
work is *coordination* cost — context that won't travel, review round-trips —
not tokens.

## Step 2 — Path, model and effort

- `DEFAULT_DELEGATION: plugin` → delegate via the Codex bridge without asking.
- `DEFAULT_DELEGATION: cli` → use the CLI handoff without asking.
- `DEFAULT_DELEGATION: ask` → ask me which to use, with the trade-off, and wait.

Even under `plugin`, recommend switching to the CLI handoff if it's clearly
better for THIS task (very large feature, or my Claude weekly limit is the hard
bottleneck). Say so in one line; don't switch silently.

**Choosing model and effort is mandatory on every delegation, not optional.**
The plugin passes neither flag unless you supply one, so a call with no flags
silently inherits whatever `~/.codex/config.toml` sets globally — typically the
strongest and most expensive option. Scale effort to how much of the task is
*undetermined*, not to how big it is, and step DOWN as often as up. **Apply the
rubric tables in `docs/codex-workflow.md`; the values below are only the floor
they adjust from.**

### Standing preferences (edit to change the defaults)
DEFAULT_DELEGATION: plugin
DEFAULT_MODEL: gpt-5.6-terra
DEFAULT_EFFORT: medium

## Steps 3–6 — Plan, execute, review, report friction

All four are in `docs/codex-workflow.md`. Two obligations survive here because
they bind even when the delegation never happens:

- **Plan first, always, regardless of path** — the standard Superpowers flow to
  `docs/plans/<feature>.md`, committed before delegating.
- **Review always, regardless of path** — an independent second pass over the
  diff, never a rubber stamp on Codex's own inline review. And when the
  integration itself misbehaves, file a report with the `codex-workflow-feedback`
  skill after the session recovers or gives up.
<!-- codex-workflow:end -->
