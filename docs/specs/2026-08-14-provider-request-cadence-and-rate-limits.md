# Provider request cadence and rate-limit handling

**Status:** Proposed improvements for Claude review and Superpowers planning. This is an analysis and
decision record, not an implementation plan.

**Written:** 2026-08-14, against the current working tree.

## Why this document exists

The Claude Code card has repeatedly reported `HTTP 429 (TooManyRequests)` from Anthropic's
undocumented OAuth usage endpoint. This document records what the application currently requests,
how often it makes those requests, what happens after failures, and the improvements agreed during
the investigation.

`docs/PRD.md` and `CLAUDE.md` remain authoritative. Claude should review this proposal, resolve any
remaining design questions, and use the normal Superpowers flow to produce an implementation plan
under `docs/plans/` before delegating code changes.

## Executive summary

- Healthy provider polling remains **60 seconds**. Five-minute healthy polling was considered and
  rejected because it makes live usage data too slow.
- The 15-second and 30-second interval choices should be removed. The effective minimum should be
  **60 seconds**, including for hand-edited settings.
- A Claude HTTP 429 should become a first-class rate-limit result rather than an ordinary HTTP error.
- When a 429 has no usable `Retry-After`, the application-authored fallback is **2 minutes, then
  4 minutes, then 8 minutes maximum** for consecutive 429 responses.
- There is **no 15-minute application-authored starting fallback**.
- When Anthropic sends a valid `Retry-After`, the application should honor that explicit server
  instruction. A server-provided 15-minute value is not the application's fallback.
- Only one request may be in flight per provider. Claude and Codex must remain concurrent with each
  other.
- Resume and Unlock refreshes should be coalesced so a normal wake-and-unlock sequence starts one
  refresh, not two.

## 1. Current request inventory

| Activity | Current implementation | Trigger | Frequency or bound | Network effect |
|---|---|---|---|---|
| Claude usage read | `GET https://api.anthropic.com/api/oauth/usage` with the local OAuth token, `anthropic-beta: oauth-2025-04-20`, and the application user agent | Scheduled Claude probe | Default every 60 seconds | One direct request to Anthropic per probe |
| Codex usage read | Starts `codex.exe app-server`, sends `initialize`, then `account/rateLimits/read` over JSONL stdio | Scheduled Codex probe | Default every 60 seconds | The local Codex process performs its own first-party read |
| Startup refresh | Forced refresh of every visible provider | Widget content rendered | Once per application start | One read per visible provider |
| Resume refresh | Forced refresh of every visible provider | Windows `PowerModes.Resume` | Once per event | One read per visible provider |
| Unlock refresh | Forced refresh of every visible provider | Windows `SessionUnlock` | Once per event | One read per visible provider |
| Global manual refresh | Forced refresh of every visible provider | Footer, tray, or Settings action | Every activation | One read per visible provider |
| Card retry | Forced refresh of one provider | `Retry now` | Every activation | One read for that provider |
| Due-check timer | Asks the refresh service whether providers are eligible | Every 5 seconds | 720 checks/hour | Local only when nothing is due |
| Presentation tick | Recomputes countdowns, ages, and display state | Every 1 second visible or 5 seconds hidden | Local display cadence | No provider request |
| Version read | Runs `claude --version` or `codex --version` | First probe or executable change | Cached by executable path and last-write time | Local process only |

Relevant code:

- `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs`
- `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs`
- `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`
- `src/AiUsageMonitor.App/Notifications/TickCadence.cs`

### 1.1 Current healthy request volume

The runtime settings inspected during this analysis used the default 60-second interval, no
per-provider overrides, and no hidden providers.

| Provider activity | Requests/hour | Requests/day if continuously running |
|---|---:|---:|
| Claude OAuth usage endpoint | Approximately 60 | Approximately 1,440 |
| Codex rate-limit reads | Approximately 60 | Approximately 1,440 |
| Combined provider probes | Approximately 120 | Approximately 2,880 |

The 5-second due-check timer is not a 5-second network poll. It performs local eligibility checks and
starts no work when neither provider is due.

## 2. Current failure behavior

`ClaudeOAuthUsageProbe` treats every non-success response other than 401 or 403 through the same
generic branch. A 429 therefore becomes:

```text
Unexpected HTTP 429 (TooManyRequests) from the usage endpoint.
```

The response's `Retry-After` header is not read. The refresh service then applies its generic
doubling backoff relative to the configured provider interval:

| Consecutive generic failures at the 60-second interval | Current next attempt |
|---:|---:|
| 1 | 1 minute |
| 2 | 2 minutes |
| 3 | 4 minutes |
| 4 and later | 8 minutes maximum |

Automatic, non-forced polls skip a provider while it is backed off or while one of its attempts is
in flight. Forced refreshes deliberately bypass both backoff and the in-flight check. Consequently:

- repeated `Retry now` activations can start concurrent requests for the same provider;
- Resume and Unlock can start two closely spaced forced refreshes;
- a global or card-level manual refresh can issue a request during rate-limit backoff;
- only the newest completion is published, but every superseded request still reaches the provider.

The application log records startup and unexpected exceptions, but normal provider attempts and
probe-authored HTTP errors are not logged. The configured rate can be calculated from code and
settings, but the exact historical number and cause of requests cannot currently be reconstructed.

## 3. Findings

| Severity | Finding | Consequence |
|---|---|---|
| High | The unofficial Claude endpoint receives approximately 1,440 healthy requests per day at the default interval | This is plausible pressure against an undocumented endpoint, although the repository cannot establish Anthropic's actual limit or whether Claude Code itself also calls it |
| High | Forced same-provider refreshes may overlap | Manual and lifecycle events can create bursts precisely while the endpoint is unhealthy |
| High | HTTP 429 and `Retry-After` are not modeled | The application cannot follow the provider's explicit cooldown instruction |
| Medium | Resume and Unlock independently force refreshes | A routine wake-and-unlock flow can duplicate work |
| Medium | 15-second and 30-second presets permit 5,760 or 2,880 Claude requests per day | Those choices are disproportionately aggressive for an unofficial quota endpoint |
| Medium | Manual retry bypasses rate-limit backoff | User interaction can unintentionally prolong a rate-limit incident |
| Medium | Request history lacks safe operational evidence | Future diagnosis relies on inference rather than recorded attempt timing and status |
| Good | Ordinary scheduled polls do not overlap in-flight work | The 5-second timer is not the source of request bursts |
| Good | Provider failures are isolated and old completions cannot overwrite newer published state | Cross-provider responsiveness and latest-result correctness are already protected |
| Good | Hidden providers are not polled | A provider the user does not consume creates no background provider traffic |

## 4. Agreed target behavior

### 4.1 Healthy cadence and settings

| Decision | Target |
|---|---|
| Default healthy interval | 60 seconds |
| Minimum effective interval | 60 seconds |
| Offered global intervals | 60 seconds, 2 minutes, 5 minutes, 10 minutes |
| Offered per-provider intervals | Default, 60 seconds, 2 minutes, 5 minutes, 10 minutes |
| Existing persisted 15-second or 30-second value | Resolve safely to 60 seconds without silently rewriting the settings file |
| Normal scheduling jitter | Optional positive jitter of 0-5 seconds; it must not materially weaken freshness |

Removing 15 and 30 only from the UI is insufficient. `AppSettings.RefreshInterval` and
`AppSettings.RefreshIntervalFor` currently clamp to a 15-second minimum, so the effective minimum
must change as well. Nearby settings and view-model tests should prove both preset removal and
hand-edited-value behavior.

### 4.2 Claude 429 handling

| Situation | Target behavior |
|---|---|
| Valid `Retry-After` delta or date | Do not issue another Claude request before the server-specified instant |
| Missing, invalid, or already-expired `Retry-After`; first consecutive 429 | Wait 2 minutes |
| Second consecutive 429 | Wait 4 minutes |
| Third and later consecutive 429s | Wait 8 minutes maximum |
| Successful Claude response | Reset the consecutive-429 counter and return to 60-second healthy polling |
| Non-429 failure | Preserve the existing generic failure policy unless the implementation plan makes and justifies a separate change |

The 2/4/8 sequence is specifically the application-authored fallback for consecutive 429 responses.
It must not be replaced by a 15-minute starting fallback.

The transport of retry advice from the Claude adapter to the provider-neutral scheduler requires a
small design decision. Any new representation must remain generic, for example a provider-neutral
`not before` instant or retry advice value. Claude-specific semantics must not leak into shared
domain, refresh, or UI code.

### 4.3 Same-provider single-flight

For a given provider, a refresh request arriving while an attempt is in flight must not start a
second provider call. Acceptable behavior is either to share the existing task or to skip the new
request. The implementation plan should choose one and test it explicitly.

This restriction is per provider only:

- Claude and Codex must continue refreshing concurrently;
- no lock may be held across a provider await;
- the existing timeout boundary remains in force;
- latest-wins sequencing may remain as defensive protection, but must no longer depend on routine
  same-provider overlap;
- the existing test that requires a manual retry to start a second in-flight attempt must be
  replaced with the new single-flight contract.

### 4.4 Manual retry behavior

- Disable or otherwise suppress `Retry now` while that provider has an active request.
- Do not allow manual retry to bypass a valid server-provided `Retry-After` instant.
- During an application-authored 2/4/8 rate-limit fallback, show the truthful next-check countdown
  rather than inviting repeated immediate requests.
- Keep ordinary manual recovery available for non-rate-limit failures where no request is active.
- A manual retry for Claude must not refresh Codex, and a manual retry for Codex must not refresh
  Claude.

### 4.5 Lifecycle refresh coalescing

Resume and Unlock frequently describe one user action. Coalesce system-triggered forced refreshes
that occur within a short window, proposed as 10-15 seconds. The coalescing scope should apply to
lifecycle triggers only; it must not silently swallow a deliberate later manual refresh, subject to
the single-flight and rate-limit rules above.

### 4.6 Credential-safe diagnostics

Add enough evidence to explain request volume and backoff without expanding the credential surface.
Useful fields include:

- provider key and safe display name;
- attempt trigger: scheduled, startup, resume, unlock, global manual, or card retry;
- attempt start and completion time;
- duration;
- safe outcome category and HTTP status code where applicable;
- consecutive generic-failure and 429 counts;
- next eligible attempt;
- whether the next attempt came from `Retry-After` or application backoff;
- number of same-provider refreshes coalesced or skipped.

Never record or display:

- OAuth tokens or authorization headers;
- raw provider response bodies or payload values;
- provider-controlled exception-message text where the existing safety boundary forbids it;
- credentials-file contents.

The implementation plan should decide whether these facts belong only in Diagnostics, in bounded
local logs, or in both. Diagnostics should remain useful even when normal informational logging is
quiet.

## 5. Lower-priority improvement

Codex currently starts a fresh `codex.exe app-server` process for every scheduled read. Reusing a
longer-lived process could reduce local process overhead, but it does not contribute to the Claude
429 and adds lifecycle and protocol complexity. It should not be included in the first rate-limit
increment unless independent profiling establishes a real need.

## 6. Suggested planning slices

These are boundaries for Claude's review, not implementation tasks ready to execute. Claude should
turn accepted slices into a normal Superpowers plan with exact paths, tests, and task ordering.

| Slice | Scope | Likely code areas |
|---|---|---|
| A. Interval floor | Remove 15/30 presets, clamp effective values to 60 seconds, preserve hand-edited settings honestly | `AppSettings`, `SettingsViewModel`, `ProviderPreferenceViewModel`, settings tests |
| B. Rate-limit advice | Parse safe `Retry-After` metadata and carry provider-neutral retry advice without exposing response content | `ClaudeOAuthUsageProbe`, domain/provider contracts, Claude probe tests |
| C. Scheduler policy | Implement 2/4/8 consecutive-429 fallback, honor server `not before`, reset on success | `ProviderRefreshService`, refresh-service tests |
| D. Single-flight and actions | Prevent same-provider overlap and make retry availability reflect in-flight/cooldown state | `ProviderRefreshService`, `MainViewModel`, provider card/view-model tests |
| E. Lifecycle coalescing | Merge closely spaced Resume and Unlock refreshes without swallowing deliberate manual action | `WidgetWindow`, WPF tests |
| F. Safe observability | Surface trigger, result, rate-limit, and next-attempt evidence without secrets or raw payloads | refresh activity/diagnostics/logging and their tests |

Slices B and C are coupled through the retry-advice contract and should be designed together even if
implemented as separate serial tasks. Slices C and D both touch `ProviderRefreshService` and must not
be delegated in parallel in the shared working tree.

## 7. Acceptance criteria for a future plan

1. A healthy Claude provider is requested no more often than once per 60 seconds, apart from the
   optional 0-5 second positive jitter.
2. Neither global nor per-provider settings offer 15-second or 30-second intervals.
3. A persisted interval below 60 seconds resolves to 60 seconds without blocking startup or being
   silently rewritten.
4. A 429 with a valid `Retry-After` produces no request before that instant.
5. Consecutive 429 responses without usable `Retry-After` schedule 2, 4, then 8 minutes, capped at
   8 minutes.
6. A successful Claude response resets rate-limit backoff and restores the healthy cadence.
7. No application-authored path begins with a 15-minute fallback.
8. At most one probe call is active for a provider, including during manual refresh.
9. Claude and Codex probes remain concurrent with each other.
10. Resume plus Unlock within the selected coalescing window results in one provider refresh cycle.
11. Retry UI cannot generate a request during an active attempt or explicit server cooldown.
12. Diagnostics identify when and why attempts occurred without exposing credentials, headers, raw
    bodies, or unsafe provider-controlled text.
13. Existing retained-row, freshness, hidden-provider, timeout, and provider-isolation behavior
    remains intact.

## 8. Non-goals

- No website scraping, browser automation, cookies, or alternate Claude usage source.
- No return to Claude status-line integration.
- No credential refresh, rewrite, persistence, or token lifecycle management.
- No five-minute default healthy cadence.
- No 15-minute application-authored starting fallback.
- No serialization of Claude and Codex behind one global refresh lock.
- No persistent Codex app-server work in the first rate-limit increment without separate evidence.
