# Provider Rate-Limit and Cadence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the widget hammering Anthropic's undocumented usage endpoint — raise the effective polling floor to 60 seconds, treat HTTP 429 as a first-class throttle with a 2/4/8-minute application fallback that always yields to a server `Retry-After`, allow at most one in-flight request per provider, pause scheduled polling while the workstation is locked, and record safe evidence of every attempt.

**Architecture:** A new provider-neutral `ThrottleAdvice` value travels on `ProviderSnapshot` from the Claude adapter to `ProviderRefreshService`. The adapter owns "the provider refused me and here is the instant it named"; the scheduler owns "how long to wait when it named nothing". No HTTP status, header name, or provider identity crosses into `Domain/`, `Refresh/`, or the UI. Single-flight, lock state, and lifecycle coalescing all live in `ProviderRefreshService` so they are testable without WPF.

**Tech Stack:** .NET 10, C# 13, WPF, xUnit, `System.Text.Json`. No new package references.

**Spec:** `docs/specs/2026-08-14-provider-request-cadence-and-rate-limits.md` — read it, including **Appendix A**, which records the live response captured during plan review.

## Global Constraints

Copied from `CLAUDE.md` and `docs/PRD.md` §4.1.1 / §21 / §23. Every task's requirements implicitly include this section.

- `dotnet build` must be **clean — warnings are errors**.
- **Run build and test as separate commands, never chained.** `dotnet build`, then `dotnet test`. A chained `build && test` is killed by the per-command time limit and loses the result of a build that already succeeded.
- **The domain model stays generic.** No property, type, or enum member may be named after a provider, an HTTP status code, a header, or a plan period. `ThrottleAdvice` must be describable without saying "Anthropic", "429", or "Retry-After".
- **Never log, persist, display, or copy a credential.** No OAuth token, `Authorization` header, raw response body, or credentials-file content in `Extra`, `Notes`, an exception message, a log line, or a diagnostic field.
- **The term "rate limit" is already taken.** In this codebase a rate limit is a *quota window* (Codex's own method is `account/rateLimits/read`). The new concept is called **throttle** throughout — types, fields, log text, and comments.
- Missing data is `null` and surfaces as `Waiting`/`Unavailable` — **never** `0`.
- Provider operations stay async, cancellable, and timeout-bounded. One provider failing must never affect the other.
- No lock may be held across a provider `await`.
- Error strings on `ProviderSnapshot.Error` are rendered verbatim on screen. Write them as UI copy — sentence case, no exception type names, no status codes.

## Deliberate omissions

State these as decided; do not implement them and do not raise them as gaps.

| Spec item | Decision |
|---|---|
| §4.1 optional 0–5s scheduling jitter | **Omitted.** Jitter exists to de-synchronise many clients; this is one single-instance desktop app, so it buys nothing and makes scheduler tests nondeterministic. The spec marks it optional and acceptance criterion 1 is satisfied without it. |
| §4.7 "HTTP status code where applicable" in diagnostics | **Omitted from the neutral contract.** A status code is meaningless for Codex, which makes no HTTP call, and putting one on `ProviderSnapshot` would leak an HTTP-shaped concept into `Domain/`. The safe outcome category plus the throttle flag carry the operational meaning; the Claude probe's own `Notes` already record the status. |
| §5.1 reset-aligned refresh (slice H) | **Deferred**, exactly as §6 of the spec directs. Not in this plan. |
| §4.6 / slice F Claude scoped-limit normalization | **Separate plan** — `docs/plans/2026-08-17-claude-scoped-limit-normalization.md`. Independent of request scheduling, and Appendix A changed its shape. |
| §5.2 persistent Codex app-server | **Not in scope.** |

## File structure

| File | Responsibility | Task |
|---|---|---|
| `src/AiUsageMonitor.Domain/ThrottleAdvice.cs` | **Create.** The provider-neutral "stop asking for a while" value. | 2 |
| `src/AiUsageMonitor.Domain/ProviderSnapshot.cs` | Carry an optional `ThrottleAdvice`. | 2 |
| `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs` | 60-second effective interval floor. | 1 |
| `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs` | Global interval presets. | 1 |
| `src/AiUsageMonitor.App/ViewModels/ProviderPreferenceViewModel.cs` | Per-provider interval presets. | 1 |
| `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs` | Recognise a throttle response; convert `Retry-After` to an instant. | 2 |
| `src/AiUsageMonitor.Infrastructure/Refresh/RefreshTrigger.cs` | **Create.** Why an attempt happened. | 3 |
| `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs` | Single-flight, trigger attribution, throttle policy, lock gate, lifecycle coalescing, attempt logging. | 3–5, 7 |
| `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs` | Pass triggers; relay lock state; feed activity to cards. | 3, 5, 6 |
| `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs` | Retry availability from live activity. | 6 |
| `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs` | Lock/unlock/resume wiring. | 5 |
| `src/AiUsageMonitor.App/ViewModels/DiagnosticsViewModel.cs` | Surface the new evidence. | 7 |

---

### Task 1: Raise the effective interval floor to 60 seconds

Slice A. Independent of every other task — no shared files.

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs:114` and `:168-171`
- Modify: `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs:16`, `:61-66`
- Modify: `src/AiUsageMonitor.App/ViewModels/ProviderPreferenceViewModel.cs:9`, `:24-29`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/AppSettingsStoreTests.cs`
- Test: `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`
- Test: `tests/AiUsageMonitor.App.Tests/ProviderPreferenceViewModelTests.cs`

**Interfaces:**
- Produces: `AppSettings.MinimumRefreshSeconds` (`public const int` = `60`). Later tasks do not depend on it, but the settings tests do.

**Requirements:**

1. Add to `AppSettings`:

```csharp
/// <summary>
/// The floor for every effective refresh interval, global or per-provider. 60 seconds because the
/// Claude Code mechanism is an undocumented first-party endpoint (CLAUDE.md) and the 15- and
/// 30-second choices this replaces permitted 5,760 and 2,880 requests a day against it.
/// </summary>
public const int MinimumRefreshSeconds = 60;
```

2. `RefreshInterval` clamps to `Math.Clamp(RefreshIntervalSeconds, MinimumRefreshSeconds, 3600)`.
3. `RefreshIntervalFor` clamps its override to `Math.Clamp(seconds, MinimumRefreshSeconds, 3600)`.
4. **Do not rewrite the settings file.** A persisted `15` stays `15` on disk and resolves to 60 seconds when read. This mirrors how `EffectiveAlertThresholds` already works: sanitized on read, never written back.
5. `SettingsViewModel.RefreshPresets` becomes `[60, 120, 300, 600]`.
6. `ProviderPreferenceViewModel.IntervalPresets` becomes `[0, 60, 120, 300, 600]` (`0` is "Shared").
7. **Both view models inject the persisted current value into their choice list when it is not a preset** (`SettingsViewModel.Durations`, `ProviderPreferenceViewModel.Durations`). Pass the *effective* value instead of the raw one, so a hand-edited `30` selects "1m" rather than adding a "30s" radio button that the clamp makes a lie:

```csharp
// SettingsViewModel constructor
RefreshIntervals = Durations(
    "refresh",
    RefreshPresets,
    (int)settings.Current.RefreshInterval.TotalSeconds,   // was: settings.Current.RefreshIntervalSeconds
    seconds => _settings.Update(s => s with { RefreshIntervalSeconds = seconds }),
    () => (int)_settings.Current.RefreshInterval.TotalSeconds);
```

```csharp
// ProviderPreferenceViewModel constructor
int current = settings.Current.RefreshSecondsOverrideFor(Key) is int seconds
    ? Math.Clamp(seconds, AppSettings.MinimumRefreshSeconds, 3600)
    : 0;
Intervals = Durations(
    $"interval-{Key}",
    current,
    SetInterval,
    () => _settings.Current.RefreshSecondsOverrideFor(Key) is int s
        ? Math.Clamp(s, AppSettings.MinimumRefreshSeconds, 3600)
        : 0);
```

**Tests to add:**

| Test | Asserts |
|---|---|
| `APersistedIntervalBelowTheFloorResolvesToSixtySeconds` | `new AppSettings { RefreshIntervalSeconds = 15 }.RefreshInterval == TimeSpan.FromSeconds(60)`; likewise `30`. |
| `APersistedPerProviderOverrideBelowTheFloorResolvesToSixtySeconds` | `RefreshIntervalFor("claude")` with an override of `15` returns 60s. |
| `AnIntervalBelowTheFloorIsNotRewrittenToDisk` | Load settings whose file holds `"refreshIntervalSeconds": 15`, save an unrelated change, reload: the file still holds `15`. |
| `NoOfferedGlobalIntervalIsBelowTheFloor` | Every `SettingsViewModel.RefreshIntervals` value is `>= 60`. |
| `NoOfferedProviderIntervalIsBelowTheFloor` | Every non-zero `ProviderPreferenceViewModel.Intervals` value is `>= 60`. |
| `AHandEditedSubFloorIntervalSelectsTheFloorChoice` | With `RefreshIntervalSeconds = 30`, the selected choice is the `60` one and no `30` choice exists. |

**Verification:** `dotnet build`, then `dotnet test`.

**Commit:** `feat: raise the effective refresh interval floor to 60 seconds`

---

### Task 2: Model a provider throttle and recognise it in the Claude adapter

Slice B. Adds the contract Task 4 consumes.

**Files:**
- Create: `src/AiUsageMonitor.Domain/ThrottleAdvice.cs`
- Modify: `src/AiUsageMonitor.Domain/ProviderSnapshot.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ClaudeOAuthUsageProbeTests.cs`

**Interfaces:**
- Produces: `AiUsageMonitor.Domain.ThrottleAdvice` — `sealed record ThrottleAdvice(DateTimeOffset? NotBefore)` with `bool IsProviderSpecified => NotBefore is not null`.
- Produces: `ProviderSnapshot.Throttle` — `ThrottleAdvice?`, an **optional trailing positional parameter defaulting to `null`** so no existing construction site changes.
- Produces: `ClaudeOAuthUsageProbe` constructor gains a trailing `Func<DateTimeOffset>? clock = null` parameter.

**Requirements:**

1. Create the domain type exactly as written:

```csharp
namespace AiUsageMonitor.Domain;

/// <summary>
/// A provider's own instruction to stop asking for a while. Provider-neutral by construction:
/// nothing here names a provider, a transport, a status code, or a header, so the scheduler can act
/// on it without learning any provider's semantics (PRD §21).
/// <para>
/// The presence of this value on a <see cref="ProviderSnapshot"/> means "this attempt was refused
/// because the caller is asking too often". <see cref="NotBefore"/> carries the provider's explicit
/// instant when it named one, and is null when the provider refused without saying for how long —
/// which is the scheduler's cue to apply its own fallback rather than invent an instant here.
/// </para>
/// <para>
/// Deliberately NOT called a rate limit. In this application a rate limit is a quota window — the
/// thing the widget displays — and Codex's own mechanism is literally
/// <c>account/rateLimits/read</c>. Overloading the term would give two unrelated concepts one name.
/// </para>
/// </summary>
public sealed record ThrottleAdvice(DateTimeOffset? NotBefore)
{
    /// <summary>
    /// Whether the provider named the instant itself. The scheduler honours a provider-specified
    /// instant exactly and never shortens it; an application-authored wait is used only when this
    /// is false.
    /// </summary>
    public bool IsProviderSpecified => NotBefore is not null;
}
```

2. Extend `ProviderSnapshot` — trailing, defaulted, so every existing call site keeps compiling:

```csharp
public sealed record ProviderSnapshot(
    string ProviderName,
    bool Installed,
    string? Version,
    string? ExecutablePath,
    ConnectionState State,
    string Mechanism,
    MechanismTier Tier,
    string? UpdateModel,
    IReadOnlyList<QuotaWindow> Windows,
    DateTimeOffset? RetrievedAt,
    string? Error,
    IReadOnlyList<string> Notes,
    ThrottleAdvice? Throttle = null);
```

3. In `ClaudeOAuthUsageProbe`, add the clock seam and use it everywhere the probe needs "now" (it currently calls `DateTimeOffset.UtcNow` inline for `RetrievedAt`):

```csharp
private readonly Func<DateTimeOffset> _clock;
// in the constructor, after the existing assignments:
_clock = clock ?? (() => DateTimeOffset.UtcNow);
```

4. Add the throttle constants and the header conversion:

```csharp
/// <summary>
/// Upper bound on a wait this probe will report from a provider-supplied instruction. The
/// instruction is honoured as given below this bound — a server that asks for 15 minutes gets 15
/// minutes, which is NOT an application fallback (spec §4.2). The bound exists only so a malformed
/// or absurd header cannot park the provider for a day; it matches the 3600s ceiling
/// AppSettings already applies to every interval.
/// </summary>
private static readonly TimeSpan MaxThrottleWait = TimeSpan.FromHours(1);

// Rendered verbatim on the card. UI copy, not diagnostics.
private const string ThrottledMessage =
    "Anthropic is asking this app to slow down — the next check is scheduled automatically.";

/// <summary>
/// Converts a Retry-After header into an absolute instant, or null when it carries nothing usable.
/// Both header forms are accepted: delta-seconds and an HTTP-date. A value that is absent, zero,
/// negative, or already in the past is "no usable instruction" rather than "retry immediately" —
/// the caller must not read null as permission to ask again at once.
/// </summary>
private static DateTimeOffset? ThrottleInstantFrom(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
{
    if (retryAfter is null)
    {
        return null;
    }

    TimeSpan wait;
    if (retryAfter.Delta is TimeSpan delta)
    {
        wait = delta;
    }
    else if (retryAfter.Date is DateTimeOffset date)
    {
        wait = date - now;
    }
    else
    {
        return null;
    }

    if (wait <= TimeSpan.Zero)
    {
        return null;
    }

    return now + (wait > MaxThrottleWait ? MaxThrottleWait : wait);
}
```

5. In `ProbeAsync`, handle 429 **before** the generic `!IsSuccessStatusCode` branch:

```csharp
if (response.StatusCode is HttpStatusCode.TooManyRequests)
{
    DateTimeOffset? notBefore = ThrottleInstantFrom(response.Headers.RetryAfter, _clock());
    notes.Add(notBefore is null
        ? "HTTP 429 (TooManyRequests); no usable Retry-After header, so the application's own wait applies."
        : "HTTP 429 (TooManyRequests); the endpoint's Retry-After instruction is being honoured.");

    return Snapshot(
        true, version, exePath, ConnectionState.Error, [], null, ThrottledMessage, notes,
        new ThrottleAdvice(notBefore));
}
```

6. Give `Snapshot(...)` a trailing `ThrottleAdvice? throttle = null` parameter and pass it through. **Do not** call `TryGetTopLevelKeys` on the 429 path — a throttle body carries no quota data and the extra parse is noise.
7. `ConnectionState` gains **no** new member. A throttle is an `Error` for display purposes; the countdown and the disabled retry button are what tell the user it is a cooldown rather than a breakage.
8. Every other status code keeps its existing generic branch verbatim.

**Tests to add** (`ClaudeOAuthUsageProbeTests`, using the existing `HttpMessageHandler` seam and a fixed clock):

| Test | Asserts |
|---|---|
| `A429WithDeltaSecondsRetryAfterReportsThatInstant` | `Retry-After: 120` at clock `T` ⇒ `Throttle!.NotBefore == T + 2min`, `IsProviderSpecified` true. |
| `A429WithAnHttpDateRetryAfterReportsThatInstant` | `Retry-After` as an HTTP-date 5 minutes ahead ⇒ `NotBefore == that date`. |
| `A429WithNoRetryAfterReportsAThrottleWithNoInstant` | `Throttle` is non-null, `NotBefore` is null, `IsProviderSpecified` false. |
| `A429WithAnExpiredRetryAfterReportsNoInstant` | An HTTP-date in the past ⇒ `NotBefore` null (**not** `now`, and not a negative wait). |
| `A429WithZeroRetryAfterReportsNoInstant` | `Retry-After: 0` ⇒ `NotBefore` null. |
| `A429ClampsAnAbsurdRetryAfterToOneHour` | `Retry-After: 86400` ⇒ `NotBefore == T + 1h`. |
| `A429IsAnErrorStateWithNoWindowsAndNoRetrievedAt` | `State == Error`, `Windows` empty, `RetrievedAt` null. |
| `A429ErrorTextNamesNoStatusCodeOrHeader` | `snapshot.Error` contains neither `"429"` nor `"Retry-After"` — it is UI copy. |
| `ANon429FailureCarriesNoThrottle` | A 500 response ⇒ `Throttle` is null and the existing generic message and top-level-key note are unchanged. |
| `ASuccessCarriesNoThrottle` | A 200 response ⇒ `Throttle` is null. |
| `AThrottleNeverCarriesResponseContent` | For a 429 whose body is `{"error":"secret-value"}`, no `Note` and no `Error` contains `secret-value`. |

**Verification:** `dotnet build`, then `dotnet test`.

**Commit:** `feat: model provider throttle advice and detect it in the Claude adapter`

---

### Task 3: Single-flight per provider, plus trigger attribution

Slice D (scheduling half). Must land before Task 4 — both edit `ProviderRefreshService`, so they are strictly serial and must never be worked in parallel.

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Refresh/RefreshTrigger.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs` (call sites only)
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`
- Test: `tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs`

**Interfaces:**
- Produces: `RefreshTrigger` enum — `Scheduled, Startup, Resume, Unlock, ManualGlobal, ManualCard`.
- Produces: `ProviderRefreshService.RefreshAllAsync(bool force, RefreshTrigger trigger, DateTimeOffset now, CancellationToken ct)`.
- Produces: `ProviderRefreshService.RefreshAsync(ProviderDescriptor provider, RefreshTrigger trigger, DateTimeOffset now, CancellationToken ct)`.
- Produces: `MainViewModel.RefreshAsync(bool force, RefreshTrigger trigger)`.
- Produces: `ProviderActivity.LastTrigger` (`RefreshTrigger?`) and `ProviderActivity.SuppressedRequests` (`int`).

**Requirements:**

1. Create the trigger enum:

```csharp
namespace AiUsageMonitor.Infrastructure.Refresh;

/// <summary>
/// Why an attempt was made. Recorded so request volume can be explained after the fact — the
/// investigation behind this work could calculate the configured rate from code but could not
/// reconstruct what had actually happened or why.
/// </summary>
public enum RefreshTrigger
{
    Scheduled,
    Startup,
    Resume,
    Unlock,
    ManualGlobal,
    ManualCard
}
```

2. **Single-flight, shared not skipped.** A refresh arriving for a provider that already has an attempt in flight returns *that attempt's task* rather than starting a second provider call. Sharing rather than skipping is what keeps `MainViewModel.IsRefreshing` honest: a manual refresh that returned instantly would flick the spinner off while a probe was still running.

   Add to `AttemptState`: `public Task? Current { get; set; }` and `public int SuppressedRequests { get; set; }` and `public RefreshTrigger? LastTrigger { get; set; }`.

   The task must be registered **under the gate**, but the probe must **not** be invoked under the gate (a probe runs synchronously until its own first await, and the spec forbids holding a lock across a provider await). Use a completion source as the placeholder:

```csharp
private Task StartRefreshAsync(
    ProviderDescriptor provider,
    bool force,
    RefreshTrigger trigger,
    DateTimeOffset now,
    CancellationToken ct)
{
    long sequence;
    TaskCompletionSource completion;

    lock (_gate)
    {
        AttemptState attempts = GetAttempts(provider);

        // Same-provider single flight. This reads only THIS provider's state, so Claude and Codex
        // are never serialised against one another (spec §4.3).
        if (attempts.Current is Task running)
        {
            attempts.SuppressedRequests++;
            return running;
        }

        if (!force && IsBackedOffUnsafe(provider, now))
        {
            return Task.CompletedTask;
        }

        sequence = ++attempts.LastStarted;
        attempts.LastAttemptStartedAt = now;
        attempts.LastTrigger = trigger;
        attempts.InFlight.Add(sequence);

        completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        attempts.Current = completion.Task;
    }

    return RunAttemptAsync(provider, sequence, now, completion, ct);
}

private async Task RunAttemptAsync(
    ProviderDescriptor provider,
    long sequence,
    DateTimeOffset now,
    TaskCompletionSource completion,
    CancellationToken ct)
{
    try
    {
        await RefreshAttemptAsync(provider, sequence, now, ct).ConfigureAwait(false);
    }
    finally
    {
        // Cleared BEFORE the shared task completes, so a waiter that resumes and immediately asks
        // again is never handed the task it was just released from.
        lock (_gate)
        {
            GetAttempts(provider).Current = null;
        }

        completion.TrySetResult();
    }
}
```

   `RefreshAttemptAsync` never throws (it already catches everything), so the shared task never faults and a sharer can await it safely.

3. Rename the existing private `IsBackedOff` to `IsBackedOffUnsafe` for consistency with `IntervalForUnsafe`/`IsHiddenUnsafe`. Behaviour unchanged.
4. Delete the now-redundant `attempts.InFlight.Count > 0` condition from the non-forced branch — `Current` supersedes it. Keep the `InFlight` set: `ProviderActivity.IsInFlight` and the superseded-publish logic still use it.
5. Thread `RefreshTrigger` through `RefreshAllAsync` and `RefreshAsync` down to `StartRefreshAsync`. **No default parameter values** — every call site names its trigger.
6. Extend `ProviderActivity` with two trailing defaulted members so existing construction stays valid:

```csharp
public sealed record ProviderActivity(
    DateTimeOffset? LastAttemptStartedAt,
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? NextAttemptAt,
    int ConsecutiveFailures,
    bool IsInFlight,
    RefreshTrigger? LastTrigger = null,
    int SuppressedRequests = 0);
```

7. Update call sites:

| Call site | Trigger |
|---|---|
| `WidgetWindow` poll timer (`_poll.Tick`) | `Scheduled`, `force: false` |
| `WidgetWindow.OnContentRendered` startup refresh | `Startup`, `force: true` |
| `MainViewModel.RefreshCommand` | `ManualGlobal`, `force: true` |
| `MainViewModel.RetryOne` | `ManualCard` |
| `WidgetWindow` Resume / Unlock handlers | leave as `Resume` / `Unlock` with `force: true` for now; Task 5 replaces both with the coalescing entry point |
| Tray and Settings refresh actions | `ManualGlobal` |

8. **Replace** the existing test `AManualRetryStartsASecondAttemptWhileTheFirstIsInFlight` (`ProviderRefreshServiceTests.cs:400`). It asserts precisely the behaviour this task removes. The replacement is `AManualRetryJoinsTheAttemptAlreadyInFlight` below.

**Tests to add or replace:**

| Test | Asserts |
|---|---|
| `AManualRetryJoinsTheAttemptAlreadyInFlight` *(replaces `AManualRetryStartsASecondAttemptWhileTheFirstIsInFlight`)* | With a probe blocked on a gate, a forced `RefreshAsync` for the same provider leaves the probe's call count at **1**; releasing the gate completes both returned tasks. |
| `ASharedRefreshCompletesWhenTheInFlightAttemptCompletes` | The task returned to the second caller is not complete while the probe is blocked, and completes once it is released. |
| `ClaudeAndCodexStillProbeConcurrently` | Keep `DifferentProvidersBeginProbingBeforeEitherCompletes` passing; add an assertion that a blocked Claude probe does not delay a Codex probe's start. |
| `SuppressedRequestsCountsRefreshesThatJoinedAnAttempt` | Two extra requests during one in-flight attempt ⇒ `ActivityFor(...).SuppressedRequests == 2`. |
| `TheTriggerOfTheLastAttemptIsRecorded` | After `RefreshAsync(provider, RefreshTrigger.ManualCard, …)`, `ActivityFor(...).LastTrigger == RefreshTrigger.ManualCard`. |
| `AProviderReleasedFromSharingCanStartAFreshAttempt` | After the shared attempt completes, a subsequent forced refresh calls the probe a second time (proves `Current` is cleared). |

**Verification:** `dotnet build`, then `dotnet test`.

**Commit:** `feat: allow one in-flight request per provider and record why each attempt ran`

---

### Task 4: Throttle scheduling policy

Slice C. Consumes Task 2's `ThrottleAdvice` and Task 3's scheduler shape.

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`

**Interfaces:**
- Produces: `NextAttemptSource` enum — `Interval, FailureBackoff, ProviderThrottle, ApplicationThrottle`.
- Produces: `ProviderRefreshService.ThrottleBackoffFor(int consecutiveThrottles)` → `TimeSpan` (`public static`).
- Produces: `ProviderActivity.ConsecutiveThrottles` (`int`) and `ProviderActivity.NextAttemptSource`.

**Requirements:**

1. Add the source enum in `ProviderRefreshService.cs`:

```csharp
/// <summary>Why the next attempt is scheduled when it is. Diagnostics needs to tell an
/// application-authored wait apart from one the provider asked for (spec §4.7).</summary>
public enum NextAttemptSource
{
    Interval,
    FailureBackoff,
    ProviderThrottle,
    ApplicationThrottle
}
```

2. The application-authored fallback is **2, 4, then 8 minutes, capped at 8**. It is a fixed ladder, not a multiple of the configured interval — the wait a provider needs has nothing to do with how often the user wants the widget updated. There is **no 15-minute starting fallback**:

```csharp
private static readonly TimeSpan[] ThrottleLadder =
[
    TimeSpan.FromMinutes(2),
    TimeSpan.FromMinutes(4),
    TimeSpan.FromMinutes(8)
];

/// <summary>
/// How long to wait after a provider refused the request without saying for how long. Fixed
/// minutes rather than a multiple of the configured interval: the wait a provider needs is a fact
/// about the provider, not about how often the user wants the widget updated.
/// </summary>
public static TimeSpan ThrottleBackoffFor(int consecutiveThrottles) =>
    ThrottleLadder[Math.Clamp(consecutiveThrottles, 1, ThrottleLadder.Length) - 1];
```

3. Add to the private `Backoff` class: `int ConsecutiveThrottles`, `DateTimeOffset? ThrottleUntil`, `NextAttemptSource NextAttemptSource`.

4. Rewrite `Record`:

```csharp
private void Record(ProviderDescriptor provider, ProviderSnapshot snapshot, DateTimeOffset now)
{
    if (!_backoff.TryGetValue(provider, out Backoff? state))
    {
        state = new Backoff();
        _backoff[provider] = state;
    }

    TimeSpan interval = IntervalForUnsafe(provider);

    if (snapshot.Throttle is ThrottleAdvice advice)
    {
        // A throttle is not a broken mechanism, so it does not feed the generic failure ladder.
        // The two counters are independent; the generic one is left where it was.
        state.ConsecutiveThrottles++;

        if (advice.NotBefore is DateTimeOffset instructed)
        {
            state.ThrottleUntil = instructed;
            state.NextAttemptSource = NextAttemptSource.ProviderThrottle;
        }
        else
        {
            state.ThrottleUntil = now + ThrottleBackoffFor(state.ConsecutiveThrottles);
            state.NextAttemptSource = NextAttemptSource.ApplicationThrottle;
        }

        // Never sooner than the healthy cadence, and never sooner than the provider asked.
        DateTimeOffset healthy = now + interval;
        state.NextAttempt = state.ThrottleUntil.Value > healthy ? state.ThrottleUntil.Value : healthy;
        return;
    }

    // Any published non-throttled outcome ends the consecutive run, which is what "consecutive"
    // means. A success and an ordinary failure both do this; only a success clears the generic
    // ladder as well.
    state.ConsecutiveThrottles = 0;
    state.ThrottleUntil = null;

    // NotInstalled and Unsupported are stable facts about the machine, not failures to retry more
    // slowly - and re-checking them costs a file-existence test.
    bool failed = snapshot.State is ConnectionState.Error or ConnectionState.Unavailable;
    state.ConsecutiveFailures = failed ? state.ConsecutiveFailures + 1 : 0;
    state.NextAttemptSource = failed ? NextAttemptSource.FailureBackoff : NextAttemptSource.Interval;
    state.NextAttempt = now + (failed ? BackoffFor(state.ConsecutiveFailures, interval) : interval);
}
```

5. **`ThrottleUntil` binds a forced refresh too.** This is the one gate a manual retry may not bypass. Add to `StartRefreshAsync`, after the single-flight check and **before** the `!force` check:

```csharp
        // The only gate a forced refresh may not bypass. An ordinary failure backoff still yields
        // to a manual retry - that is how someone recovers a provider by hand - but asking harder
        // is exactly the wrong response to being told to ask less (spec §4.4).
        if (IsThrottledUnsafe(provider, now))
        {
            attempts.SuppressedRequests++;
            return Task.CompletedTask;
        }
```

```csharp
private bool IsThrottledUnsafe(ProviderDescriptor provider, DateTimeOffset now) =>
    _backoff.TryGetValue(provider, out Backoff? state)
    && state.ThrottleUntil is DateTimeOffset until
    && until > now;
```

6. Add a public reader so the UI can tell a throttle cooldown from an ordinary backoff:

```csharp
/// <summary>
/// When this provider may next be contacted at all, including by a manual retry, or null when no
/// throttle cooldown is active.
/// </summary>
public DateTimeOffset? ThrottledUntil(ProviderDescriptor provider, DateTimeOffset now)
{
    lock (_gate)
    {
        return _backoff.TryGetValue(provider, out Backoff? state)
            && state.ThrottleUntil is DateTimeOffset until
            && until > now
                ? until
                : null;
    }
}
```

7. Extend `ProviderActivity` with `int ConsecutiveThrottles = 0` and `NextAttemptSource NextAttemptSource = NextAttemptSource.Interval` (trailing, defaulted) and populate them in `ActivityFor`.

**Tests to add:**

| Test | Asserts |
|---|---|
| `AThrottleWithAProviderInstantSchedulesExactlyThatInstant` | Probe returns `new ThrottleAdvice(T + 15min)` ⇒ `NextAttemptFor(...) == T + 15min` and `NextAttemptSource == ProviderThrottle`. Proves a server-provided 15 minutes is honoured and is not the app's fallback. |
| `AProviderInstantSoonerThanTheIntervalStillWaitsTheInterval` | `ThrottleAdvice(T + 10s)` with a 60s interval ⇒ next attempt is `T + 60s`. |
| `ConsecutiveThrottlesWithoutAnInstantWaitTwoThenFourThenEightMinutes` | Three consecutive throttles ⇒ 2min, 4min, 8min; the fourth and fifth also 8min. `NextAttemptSource == ApplicationThrottle`. |
| `NoApplicationAuthoredWaitStartsAtFifteenMinutes` | `ThrottleBackoffFor(1) == TimeSpan.FromMinutes(2)`; no value in the ladder equals 15 minutes. |
| `ASuccessAfterThrottlesRestoresTheHealthyCadence` | After two throttles, a success ⇒ `ConsecutiveThrottles == 0`, next attempt `now + interval`, `NextAttemptSource == Interval`. |
| `AnOrdinaryFailureBetweenThrottlesEndsTheConsecutiveRun` | throttle, 500-style failure, throttle ⇒ the second throttle waits **2** minutes, not 4. |
| `AThrottleDoesNotIncrementTheGenericFailureCounter` | After a throttle, `ActivityFor(...).ConsecutiveFailures == 0`. |
| `AForcedRefreshIsRefusedDuringAThrottleCooldown` | With an active `ThrottleUntil`, `RefreshAsync(provider, ManualCard, …)` does not call the probe. |
| `AForcedRefreshStillBypassesAnOrdinaryFailureBackoff` | Keep `AManualRefreshIgnoresBackoff` passing — an ordinary failure backoff remains manually recoverable. |
| `AThrottledProviderDoesNotBlockTheOtherProvider` | Claude throttled, forced global refresh ⇒ Codex is still probed. |
| `ThrottledUntilReportsNullOnceTheCooldownHasPassed` | At `until + 1s`, `ThrottledUntil(...)` is null and a forced refresh probes again. |

**Verification:** `dotnet build`, then `dotnet test`.

**Commit:** `feat: honour provider throttle instructions with a 2/4/8 minute fallback`

---

### Task 5: Lock-aware scheduling and lifecycle coalescing

Slice E.

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs:287-307`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`
- Test: `tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs`

**Interfaces:**
- Produces: `ProviderRefreshService.IsWorkstationLocked` (`bool`, get/set, gate-guarded like `HiddenProviderKeys`).
- Produces: `ProviderRefreshService.LifecycleCoalescingWindow` (`public static readonly TimeSpan` = 10 seconds).
- Produces: `ProviderRefreshService.RefreshAfterLifecycleEventAsync(RefreshTrigger trigger, DateTimeOffset now, CancellationToken ct)` → `Task`.
- Produces: `ProviderActivity.CoalescedLifecycleRefreshes` (`int`).
- Produces: `MainViewModel.SetWorkstationLocked(bool locked)` and `MainViewModel.RefreshAfterLifecycleEventAsync(RefreshTrigger trigger)`.

**Requirements:**

1. **Lock is an explicit lifecycle state, never an inactivity heuristic.** Only `SessionSwitchReason.SessionLock` / `SessionUnlock` may set it. Do not read idle time, input timers, or window visibility.

2. Add the lock gate to `StartRefreshAsync`, applied to everything the application starts *on its own behalf* — scheduled polls and lifecycle refreshes alike — and never to a refresh the user asked for:

```csharp
        // A locked workstation pauses work the application started by itself. A refresh the user
        // asked for is not that, so the two manual triggers are exempt; in practice they cannot
        // fire while the lock screen is up, but the exemption keeps the rule stated rather than
        // implied. Deliberately NOT keyed on `force`: a lifecycle refresh is forced and must still
        // be paused (spec §4.5).
        bool startedByTheApplication =
            trigger is not (RefreshTrigger.ManualGlobal or RefreshTrigger.ManualCard);

        if (startedByTheApplication && _isWorkstationLocked)
        {
            return Task.CompletedTask;
        }
```

   Place it **after** the single-flight check and **after** the throttle check. An attempt already in flight when the lock arrives is never cancelled — this gate only refuses to *start* work.

3. Add the coalescing entry point. Both Windows events describe one user action; a wake-and-unlock must produce one refresh, not two:

```csharp
/// <summary>
/// How close together two system lifecycle events must be to count as one user action. A wake
/// followed by an unlock is one person sitting down, and refreshing twice for it is the burst this
/// work exists to remove.
/// </summary>
public static readonly TimeSpan LifecycleCoalescingWindow = TimeSpan.FromSeconds(10);

/// <summary>
/// One refresh for a system lifecycle event. Coalesces events inside
/// <see cref="LifecycleCoalescingWindow"/>, and defers entirely while the workstation is locked -
/// unlock is itself a lifecycle event, so nothing is lost by waiting for it. A deliberate manual
/// refresh never comes through here and is never coalesced.
/// </summary>
public Task RefreshAfterLifecycleEventAsync(RefreshTrigger trigger, DateTimeOffset now, CancellationToken ct)
{
    lock (_gate)
    {
        // Resume while still locked: the unlock that follows will do the refresh. The coalescing
        // stamp is deliberately NOT taken here - taking it would let this skipped refresh swallow
        // the real one moments later.
        if (_isWorkstationLocked)
        {
            return Task.CompletedTask;
        }

        if (_lastLifecycleRefreshAt is DateTimeOffset last && now - last < LifecycleCoalescingWindow)
        {
            _coalescedLifecycleRefreshes++;
            return Task.CompletedTask;
        }

        _lastLifecycleRefreshAt = now;
    }

    return RefreshAllAsync(force: true, trigger, now, ct);
}
```

   Backing fields: `private bool _isWorkstationLocked; private DateTimeOffset? _lastLifecycleRefreshAt; private int _coalescedLifecycleRefreshes;`.

4. **Missed ticks must not queue.** Nothing is required for this beyond not adding a queue: the scheduler holds one `NextAttempt` instant per provider, so any number of ticks missed while locked still yields one eligible attempt on unlock. Prove it with a test rather than assuming it.

5. `IsWorkstationLocked` setter must **not** trigger a refresh. Clearing it is the window's job to sequence: unlock sets it false, *then* calls the lifecycle entry point.

6. `MainViewModel` gains:

```csharp
public void SetWorkstationLocked(bool locked) => _refresh.IsWorkstationLocked = locked;

public Task RefreshAfterLifecycleEventAsync(RefreshTrigger trigger) =>
    _lifetime.IsCancellationRequested
        ? Task.CompletedTask
        : _refresh.RefreshAfterLifecycleEventAsync(trigger, _clock(), _lifetime.Token);
```

7. `WidgetWindow` — replace the two handlers and `ForceRefreshAfterSystemEvent`:

```csharp
private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
{
    if (e.Mode == PowerModes.Resume)
    {
        Dispatcher.BeginInvoke(() => AfterSystemEvent(RefreshTrigger.Resume));
    }
}

private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
{
    switch (e.Reason)
    {
        case SessionSwitchReason.SessionLock:
            Dispatcher.BeginInvoke(() => _model.SetWorkstationLocked(true));
            break;

        case SessionSwitchReason.SessionUnlock:
            Dispatcher.BeginInvoke(() =>
            {
                // Order matters: clear the pause first, or the refresh below refuses itself.
                _model.SetWorkstationLocked(false);
                AfterSystemEvent(RefreshTrigger.Unlock);
            });
            break;
    }
}

private void AfterSystemEvent(RefreshTrigger trigger)
{
    _ = _model.RefreshAfterLifecycleEventAsync(trigger);
    OnTick(this, EventArgs.Empty);
}
```

**Tests to add:**

| Test | Asserts |
|---|---|
| `NoScheduledAttemptStartsWhileTheWorkstationIsLocked` | `IsWorkstationLocked = true`, then `RefreshAllAsync(force: false, Scheduled, …)` ⇒ probe not called. |
| `AnAttemptAlreadyInFlightIsNotCancelledByALock` | Block a probe, set locked, release ⇒ the snapshot is still published. |
| `AManualRefreshStillWorksWhileLocked` | `ManualGlobal` and `ManualCard` probe normally while locked. |
| `UnlockRequestsExactlyOneRefreshCycle` | Locked for ten scheduled ticks, then unlock ⇒ one probe call per provider, not ten. |
| `ResumeAndUnlockWithinTheWindowProduceOneRefresh` | Unlocked; `Resume` at `T` and `Unlock` at `T+3s` ⇒ one refresh cycle, `CoalescedLifecycleRefreshes == 1`. |
| `ResumeAndUnlockOutsideTheWindowProduceTwoRefreshes` | `Resume` at `T`, `Unlock` at `T+30s` ⇒ two cycles. |
| `AResumeWhileLockedIsDeferredAndDoesNotSwallowTheUnlock` | Locked, `Resume` at `T` (no refresh), unlock at `T+2s` ⇒ **one** refresh happens. This is the regression the coalescing stamp placement guards. |
| `ADeliberateManualRefreshIsNeverCoalesced` | A lifecycle refresh at `T` then `ManualGlobal` at `T+1s` ⇒ both run (the manual one shares the in-flight attempt if one is running, but is never silently dropped). |
| `AnUnlockRefreshStillObeysAThrottleCooldown` | Provider throttled until `T+5min`; unlock at `T+1min` ⇒ no probe call, and `NextAttemptFor` is unchanged. |
| `WidgetVisibilityDoesNotChangePollingCadence` | Hiding the widget leaves `NextAttemptFor` and probe counts unchanged (guards acceptance criterion 16). |

**Verification:** `dotnet build`, then `dotnet test`.

**Commit:** `feat: pause scheduled polling while locked and coalesce lifecycle refreshes`

---

### Task 6: Retry availability reflects in-flight and cooldown state

Slice D (UI half).

**Files:**
- Modify: `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs:27-36`, `:243`, and the `Tick` block that computes `NextCheckText`
- Modify: `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs:150-159`
- Test: `tests/AiUsageMonitor.App.Tests/ProviderCardViewModelTests.cs`
- Test: `tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs`

**Interfaces:**
- Produces: `ProviderCardViewModel.SetActivity(ProviderActivity activity, DateTimeOffset? throttledUntil)` — **replaces** `SetNextAttempt(DateTimeOffset?)`.
- Produces: `ProviderCardViewModel.CanRetry` (`bool`).

**Requirements:**

1. Replace `SetNextAttempt` with `SetActivity`. It is the single place the card learns what the scheduler is doing:

```csharp
/// <summary>
/// The scheduler's live view of this provider, pushed once per presentation tick. Retry
/// availability is derived here rather than guessed from connection state: a card can be in Error
/// because the mechanism broke (retry helps) or because the provider asked us to slow down (retry
/// is the one thing that must not happen).
/// </summary>
public void SetActivity(ProviderActivity activity, DateTimeOffset? throttledUntil)
{
    _nextAttempt = activity.NextAttemptAt;
    _isInFlight = activity.IsInFlight;
    _throttledUntil = throttledUntil;
    RetryCommand.RaiseCanExecuteChanged();
}

/// <summary>
/// Retry is offered for an ordinary failure and withheld while a request is already running or the
/// provider is in a cooldown. Asking harder is the wrong response to being told to ask less, and a
/// second button press cannot make an in-flight request finish sooner.
/// </summary>
public bool CanRetry => !_isInFlight && _throttledUntil is null;
```

   Backing fields: `private bool _isInFlight; private DateTimeOffset? _throttledUntil;`.

2. Give the command its guard:

```csharp
RetryCommand = new RelayCommand(() => retry(descriptor), () => CanRetry);
```

3. `NextCheckText` must stay truthful during a throttle. Its existing condition — `State is ConnectionState.Error or ConnectionState.Unavailable` — already covers a throttle, because Task 2 leaves a throttle as `Error`. Add one clause so a cooldown is shown even if a later change moves the state: compute the countdown when `_throttledUntil` is set **or** the existing condition holds.

```csharp
DateTimeOffset? showFrom = _throttledUntil ?? (_nextAttempt is DateTimeOffset next
    && next > now
    && State is ConnectionState.Error or ConnectionState.Unavailable
        ? next
        : null);

NextCheckText = showFrom is DateTimeOffset when && when > now
    ? "Next check in " + QuotaFormatting.FormatCountdown(when - now)
    : null;
```

4. `MainViewModel.Tick` feeds it:

```csharp
public void Tick()
{
    DateTimeOffset now = _clock();

    foreach ((ProviderDescriptor provider, ProviderCardViewModel card) in _cards)
    {
        card.SetActivity(_refresh.ActivityFor(provider, now), _refresh.ThrottledUntil(provider, now));
        card.Tick(now);
    }
}
```

5. `ProviderCardView.xaml` needs no change — `RetryCommand`'s `CanExecute` already drives the button's enabled state through WPF command binding. Verify the button is actually bound to `RetryCommand` and not to a click handler; if it is a click handler, bind it to the command.

**Tests to add:**

| Test | Asserts |
|---|---|
| `RetryIsUnavailableWhileARequestIsInFlight` | `SetActivity` with `IsInFlight: true` ⇒ `CanRetry` false and `RetryCommand.CanExecute(null)` false. |
| `RetryIsUnavailableDuringAThrottleCooldown` | `throttledUntil` in the future ⇒ `CanRetry` false. |
| `RetryIsAvailableForAnOrdinaryFailure` | Error state, not in flight, no throttle ⇒ `CanRetry` true. |
| `RetryBecomesAvailableAgainWhenTheCooldownPasses` | `throttledUntil` null on a later tick ⇒ `CanRetry` true and `CanExecuteChanged` was raised. |
| `AThrottledCardShowsATruthfulNextCheckCountdown` | `NextCheckText` reads `"Next check in 2m"` for a cooldown 2 minutes out. |
| `RetryingOneProviderDoesNotRefreshTheOther` | `MainViewModel` card retry for Claude ⇒ the Codex probe is not called (acceptance criterion in spec §4.4). |

**Verification:** `dotnet build`, then `dotnet test`.

**Commit:** `feat: withhold retry while a request is in flight or a cooldown is active`

---

### Task 7: Safe attempt evidence in diagnostics and logs

Slice G. The reason this whole increment exists is that request history could not be reconstructed.

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/DiagnosticsViewModel.cs:129-152`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`
- Test: `tests/AiUsageMonitor.App.Tests/DiagnosticsViewModelTests.cs`

**Interfaces:**
- Produces: `ProviderActivity.LastDuration` (`TimeSpan?`) and `ProviderActivity.LastOutcome` (`string?`).

**Requirements:**

1. **Both surfaces, for different jobs.** Diagnostics answers "what is happening now" and is the primary surface; the rolling log answers "what happened while I was away", which is the question this investigation could not answer. Neither may carry a credential, a header, a raw body, or provider-controlled text.

2. The outcome category is a **fixed application-authored vocabulary**, never provider text:

```csharp
/// <summary>
/// A safe, closed vocabulary for how an attempt ended. Application-authored on purpose: the
/// alternative is echoing provider-controlled text into a log and a diagnostics screen, and
/// ProviderSnapshot.Error is exactly the sort of string that must not go there.
/// </summary>
private static string OutcomeOf(ProviderSnapshot snapshot) => snapshot.Throttle is not null
    ? "Throttled"
    : snapshot.State switch
    {
        ConnectionState.Connected => "Success",
        ConnectionState.Stale => "Success",
        ConnectionState.Unavailable => "Unavailable",
        ConnectionState.NotInstalled => "NotInstalled",
        ConnectionState.Unsupported => "Unsupported",
        ConnectionState.Discovering => "Discovering",
        ConnectionState.Waiting => "Waiting",
        ConnectionState.Error => "Error",
        _ => "Error"
    };
```

   Every member of `ConnectionState` (`NotInstalled, Discovering, Waiting, Connected, Stale, Unavailable, Unsupported, Error`) is mapped explicitly; the discard exists only to satisfy exhaustiveness. `Stale` maps to `Success` because staleness is a freshness verdict applied later by `ConnectionStateRules`, not an outcome the probe reported.

3. Record duration and outcome in `TryPublish` (which already holds the gate and already has `now`), storing `LastDuration = now - attempts.LastAttemptStartedAt` when the start time is known.

4. Emit exactly one log line per published attempt, at `Information`. **Gather the values under the gate, then log after the `lock` block has exited** — the rolling file writer does synchronous I/O, and holding the scheduler's gate across a disk write would stall the other provider's scheduling decisions for the duration of that write. `TryPublish` returns `bool`; capture the fields into locals inside the lock and emit the line just before returning `true`.

```csharp
_logger.LogInformation(
    "Provider {Provider} attempt ({Trigger}) ended {Outcome} in {DurationMs}ms; " +
    "failures={Failures} throttles={Throttles} next={NextAttempt} because={NextAttemptSource} suppressed={Suppressed}",
    provider.DisplayName,
    attempts.LastTrigger,
    OutcomeOf(snapshot),
    duration?.TotalMilliseconds ?? 0,
    state.ConsecutiveFailures,
    state.ConsecutiveThrottles,
    state.NextAttempt,
    state.NextAttemptSource,
    attempts.SuppressedRequests);
```

   Only `provider.DisplayName` and application-authored values appear. **`snapshot.Error`, `snapshot.Notes`, and every response-derived string are forbidden here.** At the 60-second floor this is roughly 2,880 lines a day across both providers, which the existing `RollingFileWriter` already bounds.

5. Extend `ProviderActivity` with `TimeSpan? LastDuration = null` and `string? LastOutcome = null` (trailing, defaulted) and populate them in `ActivityFor`.

6. Add these rows to `DiagnosticsViewModel.BuildProviderSection`, beside the existing `Next attempt` / `Consecutive failures` / `In flight` rows:

| Row | Value |
|---|---|
| `Last attempt trigger` | `activity.LastTrigger?.ToString() ?? EmptyValue` |
| `Last outcome` | `activity.LastOutcome ?? EmptyValue` |
| `Last attempt duration` | `activity.LastDuration is TimeSpan d ? $"{d.TotalMilliseconds:0} ms" : EmptyValue` |
| `Consecutive throttles` | `activity.ConsecutiveThrottles` |
| `Next attempt reason` | `activity.NextAttemptSource` rendered as prose — `Interval` → "Normal polling interval", `FailureBackoff` → "Backing off after repeated failures", `ProviderThrottle` → "The provider asked this app to wait", `ApplicationThrottle` → "Waiting after repeated throttling" |
| `Requests joined or suppressed` | `activity.SuppressedRequests` |
| `Lifecycle refreshes coalesced` | `activity.CoalescedLifecycleRefreshes` |

**Tests to add:**

| Test | Asserts |
|---|---|
| `AnAttemptRecordsItsOutcomeCategoryAndDuration` | After a success, `LastOutcome == "Success"` and `LastDuration` is non-null. |
| `AThrottledAttemptRecordsTheThrottledOutcome` | `LastOutcome == "Throttled"` even though `State` is `Error`. |
| `TheAttemptLogLineNeverCarriesProviderText` | With a fake `ILogger` capturing messages and a probe returning `Error: "SECRET-abc"` plus a note `"SECRET-note"`, no captured log line contains either string. |
| `DiagnosticsShowsTheTriggerOutcomeAndNextAttemptReason` | The provider section contains the six new rows with the expected values. |
| `DiagnosticsNeverShowsARawResponseOrToken` | Extend the existing redaction-oriented diagnostics test to cover the new rows. |

**Verification:** `dotnet build`, then `dotnet test`.

**Commit:** `feat: record safe attempt evidence in diagnostics and the rolling log`

---

## Acceptance criteria mapping

Every criterion in spec §7 that this plan owns, and the task that proves it. Criteria 17 and 18 belong to the separate scoped-limit plan.

| § 7 | Criterion | Task |
|---:|---|---|
| 1 | Healthy Claude polling no more often than 60s | 1, 4 |
| 2 | No 15s or 30s choice, global or per-provider | 1 |
| 3 | A persisted sub-floor value resolves to 60s without blocking startup or being rewritten | 1 |
| 4 | A valid `Retry-After` produces no earlier request | 2, 4 |
| 5 | Consecutive throttles without advice schedule 2, 4, 8 minutes, capped | 4 |
| 6 | A success resets the cooldown and restores the cadence | 4 |
| 7 | No application-authored path starts at 15 minutes | 4 |
| 8 | At most one probe call active per provider, manual refresh included | 3 |
| 9 | Claude and Codex stay concurrent | 3 |
| 10 | Resume plus Unlock in the window ⇒ one refresh cycle | 5 |
| 11 | Retry UI cannot fire during an attempt or a cooldown | 4, 6 |
| 12 | Diagnostics explain when and why, with no credentials or raw bodies | 7 |
| 13 | Retained-row, freshness, hidden-provider, timeout, isolation behaviour intact | all — existing tests must stay green |
| 14 | No scheduled request starts while locked; an in-flight one may finish | 5 |
| 15 | Unlock ⇒ at most one eligible refresh, no burst, still respects single-flight and cooldown | 5 |
| 16 | Input idleness and widget visibility do not alter cadence | 5 |
