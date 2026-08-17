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
- Scheduled provider polling should pause while Windows reports that the workstation is locked and
  resume with one immediate refresh after unlock. Keyboard or mouse inactivity alone must not pause
  polling.
- Resume and Unlock refreshes should be coalesced so a normal wake-and-unlock sequence starts one
  refresh, not two.
- Claude usage responses may contain model-scoped quota windows in a `limits` array. Normalize that
  provider-specific shape before the shared extractor so those windows receive stable identities
  without duplicating existing top-level windows.
- A refresh shortly after a known quota reset is a useful follow-up improvement, but is not required
  for the first rate-limit increment.

## 1. Current request inventory

| Activity | Current implementation | Trigger | Frequency or bound | Network effect |
|---|---|---|---|---|
| Claude usage read | `GET https://api.anthropic.com/api/oauth/usage` with the local OAuth token, `anthropic-beta: oauth-2025-04-20`, and the application user agent | Scheduled Claude probe | Default every 60 seconds | One direct request to Anthropic per probe |
| Codex usage read | Starts `codex.exe app-server`, sends `initialize`, then `account/rateLimits/read` over JSONL stdio | Scheduled Codex probe | Default every 60 seconds | The local Codex process performs its own first-party read |
| Startup refresh | Forced refresh of every visible provider | Widget content rendered | Once per application start | One read per visible provider |
| Resume refresh | Forced refresh of every visible provider | Windows `PowerModes.Resume` | Once per event | One read per visible provider |
| Unlock refresh | Forced refresh of every visible provider | Windows `SessionUnlock` | Once per event | One read per visible provider |
| Workstation lock | No explicit scheduling change | Windows `SessionLock` is not currently handled as a polling state | Scheduled cadence continues while locked | The same provider traffic continues until unlock |
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
| Medium | Scheduled polling continues while the workstation is locked | Provider reads continue while the user cannot see the result, even though unlock already requests fresh data |
| Medium | An observed Claude response shape carries model-scoped quotas in a `limits` array using `percent` rather than `utilization` | The shared extractor walks the array but does not recognize `percent`; adding that key alone would produce unstable IDs such as `limits[2]` and could duplicate top-level windows |
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

### 4.5 Workstation lock and lifecycle refresh coalescing

Pause scheduled provider polling while Windows explicitly reports that the workstation is locked.
This is a lifecycle state, not an inactivity heuristic:

- do not infer a lock from elapsed keyboard or mouse inactivity;
- do not pause merely because the widget is hidden;
- do not cancel a provider request that was already in flight when the lock event arrived;
- do not start later scheduled provider requests until unlock;
- leave the normal 60-second healthy cadence unchanged while the workstation is unlocked.

On unlock, request one immediate refresh so the display catches up. That refresh remains subject to
the same-provider single-flight gate and any active server-provided `Retry-After` instant. If the
provider is still cooling down, update the truthful next-check state but do not issue an early
network request.

Resume and Unlock frequently describe one user action. Coalesce system-triggered forced refreshes
that occur within a short window, proposed as 10-15 seconds. The coalescing scope should apply to
lifecycle triggers only; it must not silently swallow a deliberate later manual refresh, subject to
the single-flight and rate-limit rules above. Lock state must not create a queued burst: multiple
missed scheduled ticks while locked still produce at most one eligible refresh after unlock.

### 4.6 Claude model-scoped quota normalization

The Claude adapter should tolerate an observed response shape in which model-scoped quota windows
are listed under a top-level `limits` array. Relevant entries can carry a percentage, reset instant,
group or period identity, and optional model scope rather than using the established top-level
`five_hour` or `seven_day` object shape.

Do not solve this by adding `percent` to the shared duck-typed key list alone. Array-position IDs are
not stable provider identities, and walking both the top-level fields and unnormalized array can
produce duplicate quota windows. Instead, normalize the Claude-specific representation inside the
Claude adapter before invoking the provider-neutral extractor:

- derive a stable window ID from a verified period or group and the scoped model identity;
- preserve an existing top-level window when it already represents the same quota;
- expose explicitly reported zero usage as zero, but never manufacture zero for a missing value;
- retain a missing reset as missing rather than inventing a reset instant;
- surface an inactive or partial scoped entry only when it has enough stable scope and period
  identity to remain understandable across refreshes;
- never copy raw response content into diagnostics, logs, UI error text, or generic provider state.

Fixture tests should cover active scoped limits, explicit zero usage, missing reset data, malformed
entries, duplicate top-level and scoped representations, multiple models, stable ordering, and
non-mutation of the parsed source data.

This normalization is independent of the 429 scheduler change and may be planned as a separate
implementation effort. Provider-specific field names and matching rules must remain inside the
Claude infrastructure adapter rather than leaking into shared domain, refresh, or UI layers.

### 4.7 Credential-safe diagnostics

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

## 5. Lower-priority improvements

### 5.1 Reset-aligned refresh

When a successful provider response supplies a trustworthy future reset instant, the scheduler may
request one refresh shortly after that instant so the new quota period appears promptly. A small
positive buffer avoids reading during the provider's reset transition.

This must be an alignment of one otherwise useful read, not a second high-frequency polling loop:

- use the earliest relevant future reset for that provider;
- retain the 60-second healthy minimum between successful provider calls;
- obey same-provider single-flight and active `Retry-After` cooldowns;
- collapse near-identical reset instants into one provider refresh;
- discard or recompute the schedule when a newer successful response changes the reset instant;
- tolerate clock changes without creating a burst or a long-lived stale schedule.

Reset alignment should be a separate follow-up slice after the first rate-limit increment. Its value
is improved reset-time freshness, not mitigation of HTTP 429 responses.

### 5.2 Persistent Codex app-server

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
| E. Lock-aware lifecycle scheduling | Pause scheduled polling on explicit workstation lock, perform one eligible refresh on unlock, and merge closely spaced Resume and Unlock triggers without swallowing deliberate manual action | `WidgetWindow`, `MainViewModel`, refresh scheduling, WPF tests |
| F. Claude scoped-limit normalization | Normalize stable model-scoped quota windows before shared extraction without duplicates, invented values, or provider-specific leakage | `ClaudeOAuthUsageProbe`, Claude fixtures/probe tests, targeted domain tests only if the shared contract changes |
| G. Safe observability | Surface trigger, result, rate-limit, and next-attempt evidence without secrets or raw payloads | refresh activity/diagnostics/logging and their tests |
| H. Reset-aligned follow-up | Schedule one guarded refresh just after a trustworthy reset instant without increasing the global healthy cadence | refresh scheduling and deterministic clock-based tests |

Slices B and C are coupled through the retry-advice contract and should be designed together even if
implemented as separate serial tasks. Slices C and D both touch `ProviderRefreshService` and must not
be delegated in parallel in the shared working tree. Slice H is explicitly lower priority and should
not delay completion of the first rate-limit increment. Slice F can be planned independently because
it changes response interpretation rather than request scheduling.

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
14. No scheduled provider request starts while the workstation is explicitly locked; an attempt
    already in flight may complete normally.
15. Unlock requests at most one eligible refresh, does not create a burst from missed ticks, and
    still respects single-flight and `Retry-After`.
16. Keyboard or mouse inactivity and widget visibility do not alter provider polling cadence.
17. Active model-scoped Claude limits receive stable IDs and are not duplicated by equivalent
    top-level windows.
18. Missing scoped-limit usage or reset values are preserved as missing rather than converted into
    invented zeroes or timestamps.

## 8. Non-goals

- No website scraping, browser automation, cookies, or alternate Claude usage source.
- No return to Claude status-line integration.
- No credential refresh, rewrite, persistence, or token lifecycle management.
- No five-minute default healthy cadence.
- No 15-minute application-authored starting fallback.
- No keyboard or mouse idle heuristic; only the explicit workstation-lock lifecycle state pauses
  scheduled polling.
- No serialization of Claude and Codex behind one global refresh lock.
- No Claude profile request, multi-account management, user-agent impersonation, or forced bypass of
  an active rate-limit cooldown.
- No persistent Codex app-server work in the first rate-limit increment without separate evidence.

---

## Appendix A — Observed Claude usage response (captured 2026-08-17)

Added during plan review. §4.6 described the `limits` array from prose; this appendix records what
the endpoint actually returned, so slice F is designed against evidence rather than a hypothesis.

Captured with a throwaway script outside the repository that read the local OAuth token, issued the
same `GET /api/oauth/usage` the application issues, and printed **key paths and value types only**.
Percentages, dollar amounts, and the token itself were never printed, stored, or committed.

### A.1 Response shape

```text
root.five_hour            : { utilization: number, resets_at: date-string,
                              limit_dollars: null, used_dollars: null, remaining_dollars: null }
root.seven_day            : { …same shape… }
root.seven_day_oauth_apps : null      root.seven_day_opus  : null
root.seven_day_sonnet     : null      root.seven_day_cowork: null
root.seven_day_omelette   : null      root.tangelo         : null
root.iguana_necktie       : null      root.omelette_promotional : null
root.cinder_cove          : null      root.amber_ladder    : null
root.nimbus_quill         : { utilization: number, resets_at: null, …dollars: null }
root.extra_usage          : { is_enabled: bool, used_credits: number, utilization: null,
                              currency: string, decimal_places: number, disabled_reason: string,
                              user_disabled: bool, spend_limit_reached: bool,
                              credits_ever_enabled: bool, daily: null, weekly: null }
root.limits               : array[2] of
                            { kind: string, group: string, percent: number, severity: string,
                              resets_at: date-string, scope: null, is_active: bool }
root.spend                : { used: { amount_minor, currency, exponent }, limit: null,
                              percent: number, severity: string, enabled: bool,
                              disabled_reason: string, cap: null, balance: null,
                              auto_reload: null, disclaimer: string,
                              can_purchase_credits: bool, can_toggle: bool }
root.member_dashboard_available : bool
```

No `Retry-After` header was present on the observed **200** response, which says nothing about what
accompanies a 429. The 429 path must therefore stay defensive rather than assume a header shape.

### A.2 The finding that changes slice F

The two `limits[]` entries are a **byte-identical re-presentation of the two top-level windows**,
not additional data:

| `limits[]` entry | `kind` | `group` | `scope` | `is_active` | Equals top-level |
|---|---|---|---|---|---|
| `limits[0]` | `session` | `session` | `null` | `true` | `five_hour` — `percent` == `utilization` **and** `resets_at` identical |
| `limits[1]` | `weekly_all` | `weekly` | `null` | `false` | `seven_day` — `percent` == `utilization` **and** `resets_at` identical |

Consequences that the prose in §4.6 could not have known:

1. **The first correct behaviour is suppression, not surfacing.** Adding `percent` to the shared
   duck-typed key list would not reveal new windows — it would render every window twice. §4.6
   predicted this trap; the capture confirms it is the actual, present-day outcome rather than a
   theoretical one.
2. **`scope` is null on the observed account**, so a model-scoped entry is unverified. Normalization
   must be written so that a scoped entry *would* be surfaced correctly, while never inventing one.
3. **`is_active: false` is not a hide signal.** `weekly_all` reports inactive while `seven_day` is
   still published top-level and still carries a real utilization and reset. Suppressing a window
   because a `limits[]` twin says `is_active: false` would blank a live quota.
4. **The `group`/`kind` vocabulary does not match the top-level key names** (`session` vs
   `five_hour`, `weekly` vs `seven_day`), so duplicate detection cannot be a name comparison. Reset
   instant plus percentage is the only observed reliable correspondence.
5. **`root.spend` carries `percent` with no reset key**, and `root.spend.used` is an object rather
   than a number. Both are correctly ignored by the shared extractor today, and must stay ignored.
6. **`root.nimbus_quill` already produces a window** — `utilization` present, `resets_at` present
   but null — exercising the "preserve unrecognised provider tokens verbatim" rule against live
   data. It is a partial window, not a defect.
