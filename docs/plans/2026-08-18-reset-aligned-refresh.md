# Reset-Aligned Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a provider reports a trustworthy future reset instant, schedule one refresh just after it, so a new quota period appears promptly without raising the healthy polling rate.

**Architecture:** `ProviderRefreshService.Record` already computes `NextAttempt` from the interval or a backoff. Reset alignment becomes one more candidate for that same instant, taken only when it is *earlier* than the value already chosen and never earlier than the 60-second floor. Nothing is persisted: the alignment is recomputed from the latest successful snapshot every time one arrives, which is what makes "discard or recompute when the reset instant changes" fall out for free rather than needing invalidation logic.

**Tech Stack:** C# / .NET 10, xUnit. No new dependencies.

**Spec:** `docs/specs/2026-08-14-provider-request-cadence-and-rate-limits.md` §5.1 (slice H in the §6 table).

## Global Constraints

- **A rate limit is a quota window; a throttle is the provider refusing us.** Never use one word for the other.
- **60 seconds is the effective polling floor** (`AppSettings.MinimumRefreshSeconds`), global and per-provider. Reset alignment may bring a read *forward* but never below this floor.
- **A throttle cooldown is the one gate even a forced refresh may not bypass.** Reset alignment must not weaken it.
- **One in-flight request per provider**, shared rather than skipped.
- Warnings are errors (`TreatWarningsAsErrors`); `dotnet build` must be clean.
- Diagnostics strings are user-facing copy — they render verbatim on screen.
- No provider-specific semantics in the scheduler. It decides *when* to ask, never what a quota means.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs` | Modify | Adds `NextAttemptSource.ResetAlignment`, two constants, one private helper, and four lines in `Record` |
| `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs` | Modify | Adds a `windows` parameter to the `Snapshot` helper, plus the alignment tests |
| `src/AiUsageMonitor.App/ViewModels/DiagnosticsViewModel.cs` | Modify | One switch arm so the new reason reads correctly instead of falling to the default |
| `tests/AiUsageMonitor.App.Tests/DiagnosticsViewModelTests.cs` | Modify | Asserts that arm |
| `src/AiUsageMonitor.App/Notifications/TickCadence.cs` | Modify | Corrects a stale comment naming a 15-second interval that slice A removed |
| `CLAUDE.md` | Modify | Records the rule in the cadence section |

---

### Task 1: Reset alignment in the scheduler

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`

**Interfaces:**
- Consumes: `ProviderSnapshot.Windows` (`IReadOnlyList<QuotaWindow>`), `QuotaWindow.ResetsAt` (`DateTimeOffset?`), `AppSettings.MinimumRefreshSeconds` (`const int` = 60, same assembly).
- Produces: `NextAttemptSource.ResetAlignment` (new enum member, consumed by Task 2); `ProviderRefreshService.ResetAlignmentBuffer` (`public static readonly TimeSpan`).

- [ ] **Step 1: Give the test `Snapshot` helper a windows parameter**

It currently hardcodes `Windows: []`, so no test can supply a reset instant.

```csharp
    private static ProviderSnapshot Snapshot(
        string name,
        ConnectionState state,
        ThrottleAdvice? throttle = null,
        string? error = null,
        IReadOnlyList<string>? notes = null,
        IReadOnlyList<QuotaWindow>? windows = null) => new(
        ProviderName: name,
        Installed: true,
        Version: null,
        ExecutablePath: null,
        State: state,
        Mechanism: "fake",
        Tier: MechanismTier.Official,
        UpdateModel: "pull (poll)",
        Windows: windows ?? [],
        RetrievedAt: state == ConnectionState.Connected ? Now : null,
        Error: error,
        Notes: notes ?? [],
        Throttle: throttle);

    private static QuotaWindow Window(DateTimeOffset? resetsAt, string id = "w") =>
        new(id, id, 50, resetsAt, null, 0, false, new Dictionary<string, string>(), false);
```

- [ ] **Step 2: Write the failing tests**

```csharp
    [Fact]
    public async Task Aligns_the_next_attempt_to_just_after_a_future_reset()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(
            Snapshot("Alpha", ConnectionState.Connected, windows: [Window(Now.AddMinutes(10))])));
        ProviderRefreshService service = ServiceWithInterval(TimeSpan.FromMinutes(30), provider);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);

        ProviderActivity activity = service.ActivityFor(provider, Now);
        Assert.Equal(Now.AddMinutes(10) + ProviderRefreshService.ResetAlignmentBuffer, activity.NextAttemptAt);
        Assert.Equal(NextAttemptSource.ResetAlignment, activity.NextAttemptSource);
    }

    [Fact]
    public async Task Does_not_delay_a_read_the_interval_would_have_taken_sooner()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(
            Snapshot("Alpha", ConnectionState.Connected, windows: [Window(Now.AddHours(5))])));
        ProviderRefreshService service = ServiceWithInterval(TimeSpan.FromMinutes(1), provider);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);

        ProviderActivity activity = service.ActivityFor(provider, Now);
        Assert.Equal(Now.AddMinutes(1), activity.NextAttemptAt);
        Assert.Equal(NextAttemptSource.Interval, activity.NextAttemptSource);
    }

    [Fact]
    public async Task Never_schedules_an_aligned_read_inside_the_sixty_second_floor()
    {
        // A reset 5 seconds away would otherwise produce a read 35 seconds after this one.
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(
            Snapshot("Alpha", ConnectionState.Connected, windows: [Window(Now.AddSeconds(5))])));
        ProviderRefreshService service = ServiceWithInterval(TimeSpan.FromMinutes(30), provider);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);

        Assert.Equal(Now.AddSeconds(60), service.ActivityFor(provider, Now).NextAttemptAt);
    }

    [Fact]
    public async Task Ignores_a_reset_that_has_already_passed()
    {
        // The loop guard. Aligning to a past instant would schedule a read that finds the same
        // instant still reported, and schedule again, forever.
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(
            Snapshot("Alpha", ConnectionState.Connected, windows: [Window(Now.AddMinutes(-1))])));
        ProviderRefreshService service = ServiceWithInterval(TimeSpan.FromMinutes(30), provider);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);

        ProviderActivity activity = service.ActivityFor(provider, Now);
        Assert.Equal(Now.AddMinutes(30), activity.NextAttemptAt);
        Assert.Equal(NextAttemptSource.Interval, activity.NextAttemptSource);
    }

    [Fact]
    public async Task Collapses_resets_that_fall_within_one_minimum_gap_into_a_single_read()
    {
        // Two windows resetting 40 seconds apart are one event: aligning to the later of them
        // means one read observes both, instead of two reads a floor apart.
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(
            Snapshot("Alpha", ConnectionState.Connected, windows:
            [
                Window(Now.AddMinutes(10), "first"),
                Window(Now.AddMinutes(10).AddSeconds(40), "second")
            ])));
        ProviderRefreshService service = ServiceWithInterval(TimeSpan.FromMinutes(30), provider);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);

        Assert.Equal(
            Now.AddMinutes(10).AddSeconds(40) + ProviderRefreshService.ResetAlignmentBuffer,
            service.ActivityFor(provider, Now).NextAttemptAt);
    }

    [Fact]
    public async Task Does_not_align_to_a_reset_reported_alongside_a_throttle()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(
            Snapshot(
                "Alpha",
                ConnectionState.Error,
                new ThrottleAdvice(Now.AddMinutes(20)),
                windows: [Window(Now.AddMinutes(2))])));
        ProviderRefreshService service = ServiceWithInterval(TimeSpan.FromMinutes(30), provider);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);

        ProviderActivity activity = service.ActivityFor(provider, Now);
        Assert.Equal(Now.AddMinutes(30), activity.NextAttemptAt);
        Assert.Equal(NextAttemptSource.ProviderThrottle, activity.NextAttemptSource);
        Assert.Equal(Now.AddMinutes(20), service.ThrottledUntil(provider, Now));
    }

    [Fact]
    public async Task Does_not_align_when_the_attempt_failed()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(
            Snapshot("Alpha", ConnectionState.Error, windows: [Window(Now.AddMinutes(2))])));
        ProviderRefreshService service = ServiceWithInterval(TimeSpan.FromMinutes(30), provider);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);

        ProviderActivity activity = service.ActivityFor(provider, Now);
        Assert.Equal(NextAttemptSource.FailureBackoff, activity.NextAttemptSource);
        Assert.Equal(Now.AddMinutes(30), activity.NextAttemptAt);
    }
```

- [ ] **Step 3: Run them and watch them fail**

Run: `dotnet test --filter FullyQualifiedName~ProviderRefreshServiceTests`
Expected: the six alignment tests fail (`ResetAlignmentBuffer` and `NextAttemptSource.ResetAlignment` do not exist yet); `Does_not_align_when_the_attempt_failed` and `Does_not_delay_a_read...` may already pass, which is fine — they are regression guards.

- [ ] **Step 4: Add the enum member**

```csharp
public enum NextAttemptSource
{
    Interval,
    FailureBackoff,
    ProviderThrottle,
    ApplicationThrottle,
    ResetAlignment
}
```

- [ ] **Step 5: Add the constants and the helper**

Add `using AiUsageMonitor.Infrastructure.Settings;` to the file's usings. Place the constants next to `ThrottleLadder`, and the helper next to `Record`.

```csharp
    /// <summary>
    /// How long after a provider's own reset instant an aligned read is taken. A read at the exact
    /// instant can still see the outgoing window if the provider's counter updates lazily, so the
    /// alignment deliberately lands just after it (spec §5.1).
    /// </summary>
    public static readonly TimeSpan ResetAlignmentBuffer = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The floor between two successful reads of one provider. Reset alignment may bring a read
    /// forward but never below this: the point is to align one read that was going to happen
    /// anyway, not to open a second, faster polling loop.
    /// </summary>
    private static readonly TimeSpan MinimumHealthyGap =
        TimeSpan.FromSeconds(AppSettings.MinimumRefreshSeconds);

    /// <summary>
    /// When this snapshot's own reset instants justify reading earlier than the interval would, or
    /// null when they do not. Derived fresh from each successful snapshot and never stored, so a
    /// provider that revises a reset instant simply produces a different answer next time - there
    /// is no cached schedule that could go stale or need invalidating.
    /// </summary>
    private static DateTimeOffset? ResetAlignedAttempt(ProviderSnapshot snapshot, DateTimeOffset now)
    {
        DateTimeOffset? earliest = null;

        foreach (QuotaWindow window in snapshot.Windows)
        {
            // Strictly future only. A reset at or before now is either already observed or a clock
            // artefact; aligning to it would schedule a read that finds the same instant still
            // reported and schedule again - exactly the high-frequency loop §5.1 forbids. This is
            // also what makes a forward clock jump harmless rather than a burst.
            if (window.ResetsAt is not DateTimeOffset resets || resets <= now)
            {
                continue;
            }

            if (earliest is null || resets < earliest)
            {
                earliest = resets;
            }
        }

        if (earliest is null)
        {
            return null;
        }

        // Resets closer together than the floor cannot each get their own read, so they are one
        // event. Anchoring to the last of them means a single read observes them all, instead of
        // one read per window a floor apart.
        DateTimeOffset anchor = earliest.Value;
        foreach (QuotaWindow window in snapshot.Windows)
        {
            if (window.ResetsAt is DateTimeOffset resets
                && resets > anchor
                && resets - earliest.Value <= MinimumHealthyGap)
            {
                anchor = resets;
            }
        }

        DateTimeOffset aligned = anchor + ResetAlignmentBuffer;
        DateTimeOffset floor = now + MinimumHealthyGap;
        return aligned < floor ? floor : aligned;
    }
```

- [ ] **Step 6: Use it in `Record`**

At the end of `Record`, after the existing `state.NextAttempt` assignment on the success path. The throttle path returns before this point, so a throttle cooldown is untouched by construction.

```csharp
        state.ConsecutiveFailures = failed ? state.ConsecutiveFailures + 1 : 0;
        state.NextAttemptSource = failed ? NextAttemptSource.FailureBackoff : NextAttemptSource.Interval;
        state.NextAttempt = now + (failed ? BackoffFor(state.ConsecutiveFailures, interval) : interval);

        // Only ever brings a read forward, never pushes one back: a provider that failed still
        // backs off, and an interval shorter than the wait to the reset still wins.
        if (!failed
            && ResetAlignedAttempt(snapshot, now) is DateTimeOffset aligned
            && aligned < state.NextAttempt)
        {
            state.NextAttempt = aligned;
            state.NextAttemptSource = NextAttemptSource.ResetAlignment;
        }
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~ProviderRefreshServiceTests`
Expected: PASS, including every pre-existing test in the file.

- [ ] **Step 8: Commit**

```bash
git add src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs
git commit -m "feat: align one refresh to a provider's reported reset instant"
```

---

### Task 2: Say so in Diagnostics, and correct the stale tick comment

**Files:**
- Modify: `src/AiUsageMonitor.App/ViewModels/DiagnosticsViewModel.cs:266-273`
- Modify: `src/AiUsageMonitor.App/Notifications/TickCadence.cs:8-14`
- Modify: `CLAUDE.md`
- Test: `tests/AiUsageMonitor.App.Tests/DiagnosticsViewModelTests.cs`

**Interfaces:**
- Consumes: `NextAttemptSource.ResetAlignment` from Task 1.

`NextAttemptReason` has a `_ =>` default returning "Normal polling interval", so the build stays clean without this change — and Diagnostics would confidently give the wrong reason for every aligned wait. That silence is the reason this task exists.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void Names_a_reset_aligned_wait_rather_than_calling_it_normal_polling()
    {
        Assert.Equal(
            "Waiting for the provider's next quota reset",
            DiagnosticsViewModel.NextAttemptReasonFor(NextAttemptSource.ResetAlignment));
    }
```

If `NextAttemptReason` is private and the test project has no accessor for it, assert through the rendered diagnostics rows instead of widening visibility — match whatever the neighbouring tests in that file already do.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --filter FullyQualifiedName~DiagnosticsViewModelTests`
Expected: FAIL — returns "Normal polling interval" via the default arm.

- [ ] **Step 3: Add the switch arm**

```csharp
        NextAttemptSource.ApplicationThrottle => "Waiting after repeated throttling",
        NextAttemptSource.ResetAlignment => "Waiting for the provider's next quota reset",
        _ => "Normal polling interval"
```

- [ ] **Step 4: Correct the stale comment in `TickCadence`**

Slice A removed the 15-second presets, so the sentence justifying this cadence names an interval that can no longer be chosen. The 5-second tick is still right, and now has a second reason.

```csharp
    /// <summary>
    /// How often the window asks the refresh service whether any provider is due. Not the refresh
    /// interval: the service owns that, per provider, so this only has to be short enough that the
    /// 60-second floor is not measurably late, and that a reset-aligned read lands close to the
    /// instant it was aligned to. A tick with nothing due costs one dictionary lookup per provider
    /// and starts no work.
    /// </summary>
    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);
```

- [ ] **Step 5: Record the rule in `CLAUDE.md`**

Under "Request cadence and throttling", add:

```markdown
- **Reset alignment only ever moves a read earlier.** When a successful snapshot reports a
  future reset, `NextAttempt` may come forward to just after it (`ResetAlignmentBuffer`,
  30 s), never below the 60-second floor and never past what the interval already chose.
  It is derived from each snapshot and never stored, so a revised reset instant needs no
  invalidation. Resets within one floor of each other collapse to a single read. A reset at
  or before `now` is ignored — aligning to a past instant re-schedules against the same
  instant forever, which is the one way this becomes the second polling loop it must not be.
```

- [ ] **Step 6: Run the full suite**

Run: `dotnet build` then `dotnet test`
Expected: clean build, 725 + new tests passing.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: name a reset-aligned wait in diagnostics"
```

---

## Deliberate omissions

| Not doing | Why |
|---|---|
| A user setting for the buffer or for alignment at all | Nothing to tune: alignment never increases request volume, so there is no cost to opt out of. A setting would be a knob whose only effect is making the widget staler. |
| Aligning on a `Stale` snapshot | `Stale` means the value is old; its reset instant is old too. Only a fresh success justifies alignment. |
| Persisting the schedule across restarts | The first refresh after launch re-derives it from a live response within seconds. Persisting it would add a stale-schedule failure mode for no gain. |
| Waking the machine, or a timer that fires at the reset instant | The existing 5-second poll tick already gives ±5 s. A dedicated timer would be a second scheduling mechanism to keep correct. |
| Aligning across providers | Reset instants are per-provider facts. Coordinating them would couple two providers this service exists to keep independent. |

## Acceptance criteria

1. A healthy provider on the default 60-second interval sees no behaviour change at all.
2. On a long interval, a reported future reset produces exactly one extra read, just after it.
3. No aligned read is ever scheduled less than 60 seconds after the read that scheduled it.
4. A reset instant at or before `now` never schedules anything.
5. An active throttle cooldown is unaffected, and a throttled response never aligns.
6. A failed attempt still backs off; alignment cannot shorten a failure backoff.
7. Two resets within 60 seconds of each other produce one read, not two.
8. Diagnostics names an aligned wait distinctly from a normal interval wait.

## Self-review

- **Spec coverage.** §5.1's six bullets map to: earliest relevant future reset (Task 1 Step 5, `earliest`); 60-second minimum (`floor`); single-flight and cooldown (unchanged code paths, asserted by `Does_not_align_to_a_reset_reported_alongside_a_throttle`); collapse (the `anchor` loop); recompute on a newer response (stateless derivation); clock changes (the strictly-future guard plus `min` against the interval).
- **Placeholders.** None; every step carries the literal code.
- **Type consistency.** `ResetAlignmentBuffer` is `public static readonly TimeSpan` in Task 1 and referenced as such in Task 1's tests. `NextAttemptSource.ResetAlignment` is added in Task 1 Step 4 and consumed in Task 2 Step 3. `Window(...)` matches `QuotaWindow`'s nine-parameter primary constructor.
- **Known soft spot.** Task 2 Step 1 assumes a test accessor for `NextAttemptReason` that may not exist; the step says to follow the neighbouring tests rather than widen visibility, and this must be checked against the real file rather than assumed.
