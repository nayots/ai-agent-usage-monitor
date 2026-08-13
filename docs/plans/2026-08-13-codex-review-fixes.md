# Codex review fixes — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development`
> (recommended) or `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Fix the seven correctness defects that the independent Codex reading recorded in
`docs/specs/2026-08-13-feature-inventory-and-ideas.md` §3. Feature proposals from that document
are explicitly **out of scope**.

**Architecture:** No new layers. Three of the fixes tighten existing seams
(`ProviderRefreshService`, `ProviderCardViewModel`, `SettingsService`); three extract a pure,
publicly testable helper out of code that is currently untestable because it launches a process or
formats an exception (`CodexExecutableLocator`, `CodexProtocol`, `ProviderErrorText`); one adds two
members to `IProviderProbe`.

**Tech Stack:** .NET 10, C#, WPF, xUnit, `System.Text.Json`. No new package references.

**Source of truth for each defect:** the numbered entry in
`docs/specs/2026-08-13-feature-inventory-and-ideas.md` §3, cited per task. `docs/PRD.md` outranks
both.

## Global Constraints

Every task's requirements implicitly include this section.

- **Warnings are errors.** `dotnet build` must be clean.
- **Run `dotnet build` and `dotnet test` as separate shell commands, never chained.** A chained
  invocation spends one per-command time budget on both and can be killed before it emits anything.
- **Error strings are UI copy.** A `ProviderSnapshot.Error` is spliced verbatim into a notice body
  and rendered on screen. It must be app-authored, short, and must never carry a raw response body,
  raw JSON, headers, a file path fragment, or anything derived from a credential.
- **Credentials are in-memory only.** Never log, persist, cache, display or copy a token; never put
  one in `Notes`, `Extra`, an exception message, or a diagnostic dump.
- **Missing data is `null`,** surfacing as `Waiting`/`Unavailable` — never `0`, never a placeholder.
- **Every mechanism carries a visible tier.** A value obtained unofficially must never be presented
  as official, and an official mechanism must never be labelled unofficial.
- **The domain model stays provider-neutral.** No property named after a plan period; no
  provider-specific semantics in `Domain/` or in any view model.
- **Launch the vendored `codex.exe` directly, never the npm shim** (`codex.cmd` / `codex.ps1`).
  `ProcessRunner` sets `UseShellExecute = false`, which cannot execute a `.cmd` at all.
- **No hardcoded user paths.** Resolve per-user locations at runtime via
  `Environment.GetFolderPath`. The app must run for a user who is not the author.
- **No new `PackageReference`.** No telemetry, scraping, browser automation, or admin rights.
- **One commit per task**, message in this repo's style (lowercase `fix:` / `refactor:` prefix,
  short subject, body explaining the defect and the fix).
- The test projects have **no `InternalsVisibleTo`**. Anything a test must reach is `public`, as
  `ClaudeExecutableLocator` already is.

---

## File structure

| File | Responsibility | Task |
|---|---|---|
| `src/AiUsageMonitor.Domain/IProviderProbe.cs` | Gains `Mechanism` and `Tier` — stable facts about the mechanism, not about the last call | 1 |
| `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs` | Stable failure metadata (1); single-flight and latest-wins (3) | 1, 3 |
| `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexExecutableLocator.cs` | **New.** Resolves the vendored `codex.exe`; never returns a shim | 2 |
| `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProtocol.cs` | **New.** Pure frame handling for the app-server JSONL protocol | 5 |
| `src/AiUsageMonitor.Infrastructure/Providers/ProviderErrorText.cs` | **New.** Maps an exception to one app-authored line | 6 |
| `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs` | Uses the three helpers above; stops emitting `ex.Message` and raw JSON | 1, 2, 5, 6 |
| `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs` | Stops emitting `ex.Message` | 1, 6 |
| `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs` | Retains the last good rows through a failure | 4 |
| `src/AiUsageMonitor.App/ViewModels/ProviderNotice.cs` | Bounds the appended reason | 6 |
| `src/AiUsageMonitor.Infrastructure/Settings/SettingsService.cs` | Records that a save failed | 7 |
| `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs` + `Views/SettingsWindow.xaml` | Says a change is session-only | 7 |

---

## Task 1: Stable mechanism and tier on service-authored failures

**Defect (X3, X17):** `ProviderRefreshService.Failed` hardcodes `Mechanism: "unknown"` and
`Tier: MechanismTier.Unofficial`. A service-level **timeout of the Official Codex probe therefore
flips its badge to Unofficial** — the app mislabels its own tier exactly when the user is looking at
a failure. The tier is a property of the mechanism, not of whether the last call succeeded.

**Files:**
- Modify: `src/AiUsageMonitor.Domain/IProviderProbe.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs` (`Failed`)
- Modify (test doubles — all seven implement the interface):
  `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`,
  `tests/AiUsageMonitor.App.Tests/{ProviderCardViewModelTests,MainViewModelTests,TrayGlyphStateTests,UsageAlertWatcherTests,WidgetWindowTests,ViewLoadingTests}.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`

**Interfaces produced:**

```csharp
public interface IProviderProbe
{
    string Name { get; }

    /// <summary>
    /// How this probe reads usage, in the same words its own snapshots carry. A stable fact about
    /// the mechanism, so a failure the probe never got to author can still be labelled honestly.
    /// </summary>
    string Mechanism { get; }

    /// <summary>Official or Unofficial. A property of the mechanism, never of the last call.</summary>
    MechanismTier Tier { get; }

    Task<ProviderSnapshot> ProbeAsync(CancellationToken ct);
}
```

**What changes:**
- Both real probes expose their **existing** values — `CodexProbe`: the current private
  `Mechanism` const and `MechanismTier.Official`; `ClaudeOAuthUsageProbe`: the current private
  `Mechanism` const (`"Anthropic OAuth usage endpoint (UNOFFICIAL/undocumented)"`) and
  `MechanismTier.Unofficial`. **Do not invent new wording** and do not change the strings the
  snapshots already carry; the private const becomes the public property's backing value.
- `ProviderRefreshService.Failed` uses `provider.Probe.Mechanism` and `provider.Probe.Tier`.
- Test doubles get whatever the test already asserts; where a test asserts nothing about tier,
  `MechanismTier.Official` and a short fake mechanism string are fine.

**Leave alone:** `Installed: true` in `Failed`. Nothing in the app reads `ProviderSnapshot.Installed`
(only the POC's console printout does), and a timed-out probe genuinely does not know whether
discovery succeeded, so any other value would be a different guess rather than a better one.

**Acceptance criteria:**
- A descriptor whose probe declares `Official` + mechanism `"m"`, whose `ProbeAsync` never returns,
  yields a snapshot with `Tier == MechanismTier.Official` and `Mechanism == "m"` after the timeout.
- The same holds when `ProbeAsync` throws.
- No test asserts `Mechanism == "unknown"` any more.
- `dotnet build` clean; `dotnet test` green.

---

## Task 2: Never launch the npm shim — testable Codex discovery

**Defect (X23):** `CodexProbe.DiscoverExecutable()` ends `return FindOnPath("codex.cmd");`. That
returns the npm shim, which `CLAUDE.md` explicitly forbids launching, into `ProcessRunner` and
`ReadRateLimitsAsync` — both of which set `UseShellExecute = false` and **cannot execute a `.cmd`
at all**. On any machine without the vendored path the fallback is dead code that reports a
confusing failure instead of resolving the executable. There is no test for the branch.

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexExecutableLocator.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs` — delete
  `DiscoverExecutable`, `SafeEnumerateDirectories` and `FindOnPath`; call the locator; update the
  `NotInstalled` snapshot's `Notes` to describe what is actually checked now
- Create: `tests/AiUsageMonitor.Infrastructure.Tests/CodexExecutableLocatorTests.cs`

**Interfaces produced:**

```csharp
public static class CodexExecutableLocator
{
    /// <summary>
    /// npm prefixes to search, in priority order: %APPDATA%\npm first, then every PATH directory
    /// holding a codex shim. Pure - takes the filesystem test as a delegate so ordering, which is
    /// the part that silently regresses, is testable without touching disk.
    /// </summary>
    public static IReadOnlyList<string> ShimDirectories(
        string appData, string? pathEnvironment, Func<string, bool> fileExists);

    /// <summary>The vendored codex.exe under one npm prefix, or null. Touches the filesystem.</summary>
    public static string? VendoredExecutableUnder(string npmPrefix);

    /// <summary>
    /// The first real codex executable found, or null. Never returns a .cmd or .ps1 shim: the
    /// launch paths set UseShellExecute = false and cannot run one.
    /// </summary>
    public static string? Locate();
}
```

**Resolution order for `Locate()`:**
1. `VendoredExecutableUnder(%APPDATA%\npm)` — the verified location.
2. A literal `codex.exe` on PATH — a real executable, so usable directly.
3. For each PATH directory holding `codex.cmd` or `codex.ps1`: `VendoredExecutableUnder(thatDirectory)`.
   This generalises the existing hardcoded `%APPDATA%\npm` search to any npm prefix.
4. `null` — `NotInstalled`.

**`VendoredExecutableUnder(prefix)` looks under
`prefix\node_modules\@openai\codex\node_modules\@openai\`:**
1. the exact verified path `codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe`;
2. otherwise a glob over `codex-win32-*\vendor\*\bin\codex.exe`, returning the first that exists.

Keep the existing `IOException` / `UnauthorizedAccessException` tolerance when enumerating: an
unreadable directory is not a reason to stop looking at the rest.

**Acceptance criteria** (build the trees under `TempDirectory`, which the test project already has):
- The exact vendored layout resolves.
- An arm64-shaped layout (`codex-win32-arm64\vendor\aarch64-pc-windows-msvc\bin\codex.exe`)
  resolves through the glob.
- A prefix containing **only** `codex.cmd` resolves to `null` from `VendoredExecutableUnder`, and
  the shim path is never returned by anything.
- **Explicit assertion:** no path returned by any member ends in `.cmd` or `.ps1`.
- `ShimDirectories` puts `%APPDATA%\npm` first, includes PATH directories holding a shim, and skips
  empty/whitespace PATH entries.
- A prefix with nothing in it resolves to `null`.
- The `NotInstalled` note no longer claims `codex.cmd on PATH` is a resolution step.

---

## Task 3: Single-flight, latest-wins refresh

**Defect (X2):** the poll timer, the footer/tray/settings refresh and the per-card retry all call
into the refresh service independently. Nothing sequences them, so a slow earlier attempt can
complete *after* a newer one and overwrite both the card and the backoff state — the visible
snapshot and the consecutive-failure count can move backwards in time.

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`

**What changes** — per provider, under the existing `_gate`:
- An attempt takes a monotonically increasing sequence number when it starts.
- On completion, an attempt whose number is **lower than the highest already published** is
  discarded entirely: no `Refreshed` event, no `Record` call, no backoff mutation. Log it at debug
  or trace level and return.
- A **non-forced** request (`RefreshAllAsync(force: false, …)`, i.e. the poll timer) skips a
  provider that already has an attempt in flight, exactly as it already skips one in backoff.
- A **forced** request always starts a new attempt, even with one in flight — a manual retry against
  a hung probe is the case that has to work — and the hung attempt is superseded when it lands.
- The existing shutdown path (`return` when `ct.IsCancellationRequested`) must still leave all state
  untouched, and must clear its in-flight marker.

**Do not** introduce a lock held across `await`, and do not serialise providers against each other:
concurrency *between* providers is a requirement (PRD §4.5), and only same-provider overlap is the
defect.

**Acceptance criteria:**
- Two overlapping attempts for one provider, the first completing last: only the second snapshot is
  raised, and the first raises nothing. (Drive with `TaskCompletionSource`.)
- An old **failure** completing after a new **success** leaves `ConsecutiveFailures` at 0 — assert
  through observable behaviour, e.g. the provider is not backed off on the next non-forced cycle.
- With an attempt in flight, `RefreshAllAsync(force: false, …)` does not call the probe again
  (assert the fake's call count).
- With an attempt in flight, `RefreshAsync(provider, …)` (manual retry) does call the probe again.
- Two different providers still probe concurrently, and one throwing still does not stop the other —
  the existing tests covering this must keep passing unmodified.

---

## Task 4: Retain the last good rows through a transient failure

**Defect (X1, X16):** `ProviderCardViewModel.Apply` always calls `RebuildWindows(snapshot)`, and both
probes return an empty `Windows` list on failure — so **one dropped request erases the last valid
reading**. PRD §14 ends: *"The widget must never silently replace stale data with an empty or
apparently current value."* Keeping only `_lastSuccessAt` preserves the timestamp and throws away
the part the user was reading. Worse, the card then says `Last succeeded 4m ago` next to no rows at
all, which reads as data loss rather than as a retry in progress.

**Files:**
- Modify: `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs`
- Test: `tests/AiUsageMonitor.App.Tests/ProviderCardViewModelTests.cs`

**What changes:**
- Hold the displayed windows in their own field (e.g. `IReadOnlyList<QuotaWindow> _rows = []`) plus a
  flag for whether they came from the current snapshot or are retained.
- `Apply` chooses the rows before rebuilding:
  - snapshot has windows → use them, retained flag false;
  - snapshot has none **and** `State is ConnectionState.Error or ConnectionState.Unavailable` →
    keep the previous rows, retained flag true;
  - snapshot has none in any other state (`Connected`, `Stale`, `Waiting`, `NotInstalled`,
    `Unsupported`) → clear. A *successful* read reporting zero windows is a real answer and must
    still produce the existing "No quota windows reported" notice.
- `RebuildWindows()` reads that field rather than taking a snapshot parameter. This is what stops the
  `ColorBarsByUsage` setter — which rebuilds from `_snapshot` — from erasing retained rows.
- Retained rows must **render stale**: add a distinct predicate (e.g. `RowsAreStale => IsStale ||
  retained`) and feed it to each `QuotaRowViewModel.IsStale` in both `RebuildWindows` and `Tick`.
  **Do not widen the card's own `IsStale`** — it drives the card's chrome and state chip, and a
  failing card must keep saying `Error`, not `Stale`.
- Update the `TimestampLine` doc comment: its current text ("A card whose rows are gone reports how
  long it has been failing instead") describes behaviour this task changes. The *logic* is already
  right — a failure snapshot has a null `RetrievedAt`, so the line becomes `Last succeeded {age}`,
  which now correctly dates the rows on screen.

**Do not** add a retention expiry, a new setting, or persistence. The rows are visibly stale, the
failure notice is directly above them, and the age is on the card — the user has what they need to
judge. Expiry is a separate decision recorded as X13/X14 in the ideas document.

**Acceptance criteria:**
- Connected snapshot with two windows, then an `Error` snapshot with none → both rows still present,
  each with `IsStale == true`, the error notice showing, `State == ConnectionState.Error`, and the
  timestamp reading `Last succeeded …`.
- The same for `Unavailable`.
- Connected snapshot with two windows, then a **Connected** snapshot with none → rows cleared, and
  the "No quota windows reported" notice appears.
- Rows are cleared on `NotInstalled`, `Unsupported` and `Waiting`.
- A later successful snapshot replaces retained rows and clears the retained flag (rows no longer
  stale).
- Toggling `ColorBarsByUsage` while retained rows are displayed keeps them.
- `HasWindows` stays consistent with what is displayed.

---

## Task 5: Pure frame handling for the Codex app-server protocol

**Defect (X5, part of X6/X19):** the JSONL read loop lives inside a method that launches a process,
so none of it is tested — including the trap `CLAUDE.md` documents (interleaved notifications, absent
`"jsonrpc"`, non-matching ids). It also throws
`$"codex app-server returned an error: {errorEl.GetRawText()}"`, putting **raw provider JSON straight
onto a card**.

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProtocol.cs`
- Create: `src/AiUsageMonitor.Infrastructure/Providers/ProviderMechanismException.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs` — the read loop calls it
- Create: `tests/AiUsageMonitor.Infrastructure.Tests/CodexProtocolTests.cs`

**Interfaces produced:**

```csharp
/// <summary>
/// One app-server frame at a time. Pure and public so the protocol's documented traps are testable
/// without launching codex.exe: responses omit "jsonrpc" entirely, and unsolicited notifications
/// interleave before the answer.
/// </summary>
public static class CodexProtocol
{
    /// <summary>
    /// True when this line is the id:2 result. False for notifications, other ids and unparseable
    /// lines - keep reading. Throws <see cref="ProviderMechanismException"/> on an id:2 error frame.
    /// Appends observations to <paramref name="notes"/>, key names only, never values.
    /// </summary>
    public static bool TryReadResult(string line, List<string> notes, out JsonElement result);
}
```

Add alongside it:

```csharp
/// <summary>
/// A failure whose Message this application authored, and which is therefore safe to render on a
/// card verbatim. Nothing derived from provider output may be interpolated into it.
/// </summary>
public sealed class ProviderMechanismException(string message) : Exception(message);
```

(place `ProviderMechanismException` in `src/AiUsageMonitor.Infrastructure/Providers/`.)

**Error-frame copy:** app-authored, carrying at most the numeric JSON-RPC `code`:
`"The Codex app-server rejected the rate-limit request (error {code})."`, or without the code
`"The Codex app-server rejected the rate-limit request."`. The error object's **top-level key names
only** may go into `notes` — mirror `ClaudeOAuthUsageProbe.TryGetTopLevelKeys`, which already
establishes the "key names, never values" rule. `GetRawText()` must not appear anywhere in the file
after this task.

The existing "closed stdout before an id:2 response" failure also becomes a
`ProviderMechanismException`; its wording is already app-authored, keep it.

**Acceptance criteria:**
- A frame with no `id` and a `method` returns false and adds a "skipped notification" note naming the
  method.
- A frame with `id:1` returns false and adds no note.
- A malformed/non-JSON line returns false and does not throw.
- A blank line returns false.
- `{"id":2,"result":{…}}` — **note the absent `"jsonrpc"`** — returns true and yields the result
  element; the returned element must remain usable after the source document is disposed.
- `{"id":2,"error":{"code":-32600,"message":"Not initialized"}}` throws
  `ProviderMechanismException` whose message contains `-32600` and contains **neither** `{` nor the
  string `Not initialized`; the note lists `code, message` and not their values.
- A JSON array line, and a line whose `id` is a string rather than a number, both return false.
- `CodexProbe` behaviour through the real process is unchanged — do not rewrite the process launch,
  pipelining, stdin close or `TryKill` backstop in this task.

---

## Task 6: App-authored, bounded error copy

**Defect (X5, C6):** `CodexProbe` returns `Error: ex.Message` from its generic catch, and
`ClaudeOAuthUsageProbe` returns `$"HTTP request failed: {ex.Message}"`. Both land verbatim in a
notice body on an always-visible card. Provider- and framework-authored text is unbounded and may
carry paths or payload fragments. `ProviderRefreshService` already solved this for its own backstop
— type name only, full exception to the log — and the probes should be held to the same rule.

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Providers/ProviderErrorText.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs` (generic catch)
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs`
  (the `HttpRequestException` catch)
- Modify: `src/AiUsageMonitor.App/ViewModels/ProviderNotice.cs` (`Compose`)
- Create: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderErrorTextTests.cs`
- Test: `tests/AiUsageMonitor.App.Tests/` — a `Compose` bound test (new file or an existing suitable one)

**Interfaces produced:**

```csharp
/// <summary>
/// One app-authored line describing a probe failure, fit to render on a card. Never returns text
/// this application did not write: an arbitrary exception message can carry a path, a payload
/// fragment, or a header, and a card is always visible.
/// </summary>
public static class ProviderErrorText
{
    public static string For(Exception ex);
}
```

**Mapping:**

| Exception | Copy |
|---|---|
| `ProviderMechanismException` | its own `Message` — this application wrote it |
| `HttpRequestException` with `HttpRequestError.NameResolutionError` | `"The usage endpoint could not be resolved. Check the network connection."` |
| `HttpRequestException` with `HttpRequestError.ConnectionError` | `"The usage endpoint could not be reached."` |
| `HttpRequestException` with `HttpRequestError.SecureConnectionError` | `"The secure connection to the usage endpoint failed."` |
| `HttpRequestException` (any other) | `"The request to the usage endpoint failed."` |
| `System.ComponentModel.Win32Exception` | `"The provider executable could not be started."` |
| `IOException` | `"Communication with the provider executable failed."` |
| `JsonException` | `"The provider returned a response that could not be read."` |
| anything else | `$"The provider probe failed unexpectedly ({ex.GetType().Name})."` — the wording `ProviderRefreshService` already uses |

Order the checks so the more specific type wins (`JsonException` before `IOException` is not needed —
they are unrelated — but `HttpRequestException` must be tested before any base type).

`ProviderNotice.Compose` gains a hard bound: append at most 200 characters of the reason, ending with
a single `…` when truncated. This is a belt-and-braces stop on an error string this app did not
author reaching the card at full length; it is not a substitute for the mapping above.

**Acceptance criteria:**
- Each row of the table returns exactly that copy.
- **Leak assertion:** `ProviderErrorText.For(new HttpRequestException("token=sk-secret-abc123"))`
  returns a string containing neither `sk-secret-abc123` nor `token=`.
- The fallback names the exception type and nothing else from the exception.
- `Compose` with a 500-character reason returns at most `lead.Length + 1 + 201` characters and ends
  with `…`; a short reason is passed through unchanged; a null/whitespace reason is still omitted
  entirely rather than replaced by a placeholder.
- `ex.Message` no longer appears in any `Error:` argument in either probe. It may still be used in
  `Notes` for the `--version` helpers, which are already type-scoped and non-fatal — leave those.
- **Not in scope:** the "Details" affordance from C6. That is a UI feature awaiting a decision.

---

## Task 7: Say when a settings change is session-only

**Defect (X9):** `SettingsService.Update` applies the change, announces it, then catches a save
failure and writes only a log warning. Live apply therefore looks successful while the next launch
silently reverts it, and the log is discoverable only by a user who already suspects the problem.

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Settings/SettingsService.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/SettingsServiceTests.cs`,
  `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`

**What changes:**
- `SettingsService` exposes whether the last save failed (e.g. `bool PersistenceFailed` plus a change
  notification the view model can subscribe to). Set on a caught save failure, cleared on the next
  successful save. The existing ordering — apply, announce, then persist — is deliberate and stays;
  so does the existing exception filter.
- `SettingsViewModel` projects it as a visibility flag plus fixed app-authored copy:
  *"Changes apply to this session only — the settings file could not be saved."* The window already
  has an **Open logs** action for the detail; do not duplicate it and do not put the exception
  message, the file path or the exception type in the copy.
- `SettingsWindow.xaml` renders it as a non-modal inline line, using the existing alert brush from
  the theme tokens. No dialog, no blocking, and it must not change the window's size-to-content
  behaviour (`SettingsWindow.xaml.cs` caps height against the work area; the line appears inside the
  existing scroll/content region).
- Subscribe and unsubscribe symmetrically — `SettingsViewModel` is disposed by
  `SettingsWindow.OnClosed`, and a leaked handler on the process-lifetime service outlives the window.

**Acceptance criteria:**
- A store whose `Save` throws `IOException` → `Current` is still updated, `Changed` is still raised,
  and the failure flag is set.
- A subsequent successful save clears the flag.
- The view model's flag and copy follow the service.
- The warning text contains no path, no exception type and no exception message.
- The settings window still opens, sizes to content and closes without leaking the subscription.

---

## Out of scope — recorded, not forgotten

These are in the ideas document and stay there awaiting a decision in its §4. Do not implement them
here, and do not partially start them.

| Entry | Why not now |
|---|---|
| X4 (render `IsPartial` and the exact reset instant) | A row-content feature, not a defect |
| X6 (full injectable HTTP/process boundaries for both probes) | Tasks 2, 5 and 6 deliver the testable slices this plan needs; the rest is its own increment |
| X7/X18 (monitor work-area and DPI-aware placement recovery) | Needs display-change and DPI-change handling — a real increment, not a fix |
| X8/X21 (lifecycle-aware tick and poll cadence) | Behaviour change with visible consequences; needs the "next check" copy from C2 first |
| X10–X15 (diagnostics, reset flow, zero-provider state, restored snapshot, history, localization) | Features |
| C6's "Details" affordance | UI feature; Task 6 does the safety half |

## Verification

Run as **separate** commands, never chained:

```powershell
dotnet build
dotnet test
```

Both must be clean; the suite is 391 tests before this plan and must only grow.
