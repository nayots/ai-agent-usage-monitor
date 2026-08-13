# Provider Boundaries and Idle Cost — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put injectable process and HTTP boundaries under the two real provider probes, cover them with contract tests, and stop relaunching each provider executable for its version on every poll.

**Architecture:** Both probes currently reach the outside world through a static `ProcessRunner` and a static `HttpClient`, and both resolve their own executable and credential paths. That is why neither has a single direct test. This increment introduces narrow seams — an `IProcessRunner` for launching, an injectable `HttpMessageHandler` for the one permitted host, and injectable path/locator delegates — each defaulting to today's behaviour so no production wiring changes. On top of those seams sits a version cache keyed on the executable's path *and* its last-write time, which removes two process launches per poll cycle.

**Tech Stack:** .NET 10, C#, xUnit. No new `PackageReference` in any project.

This increment implements **X6** (the remainder — injectable HTTP/process boundaries and adapter contract tests) and **C1** (cache `--version`) from `docs/specs/2026-08-13-feature-inventory-and-ideas.md`. It is increment 1 of 3; increments 2 and 3 are `2026-08-13-cadence-and-alerts.md` and `2026-08-13-row-detail-and-window-reach.md`.

## Global Constraints

Every task's requirements implicitly include this section. These are product requirements from `CLAUDE.md` and `docs/PRD.md` §4.1.1 / §23, not style preferences.

- **`dotnet build` must be clean. Warnings are errors.**
- **Run `dotnet build` and `dotnet test` as separate commands, never chained.** A chained command spends the whole per-command time limit on one call and can be killed before emitting anything.
- **No new `PackageReference` in any project.** If a task appears to need one, stop and report.
- **No `InternalsVisibleTo` anywhere.** No test project has it. Anything a test must reach is `public` (as `ClaudeExecutableLocator` and `CodexExecutableLocator` already are).
- **Credentials are used in-memory only.** The OAuth token is read into one local, used once for one header, and never logged, persisted, cached, displayed, copied, or placed in `Notes`, `Extra`, an exception message, or a test assertion message. This application never refreshes or rewrites a credential.
- **`https://api.anthropic.com/api/oauth/usage` is the only network destination this program may reach.** It stays hardcoded, never derived from configuration, a redirect, or provider input. `AllowAutoRedirect` stays `false`. An injected handler is for tests only and must never make the production path reach a different host.
- **Any string assigned to `ProviderSnapshot.Error` is rendered verbatim on a visible card.** Treat it as UI copy: app-authored, one line, no raw response body, no headers, no paths, no provider protocol object. Route exception-derived text through `ProviderErrorText.For`.
- **Missing data is `null`, surfacing as `Waiting`/`Unavailable` — never `0`, never a placeholder.**
- **Never launch the npm shim.** The launch paths set `UseShellExecute = false`, which cannot execute a `.cmd` or `.ps1` at all. Executable resolution stays in `CodexExecutableLocator` / `ClaudeExecutableLocator`.
- **No hardcoded user paths.** Resolve per-user locations at runtime via `Environment.GetFolderPath`. Someone who is not the author, on a machine that is not the author's, must be able to run the release artifact.
- **The domain stays provider-neutral.** No property named after a plan period. Window count, names and durations are discovered, never assumed.
- **Every mechanism carries a visible tier.** A value obtained unofficially is never presented as official.
- **Provider operations are async, cancellable and timeout-bounded.** One provider failing must never affect the other or crash the process.
- **One commit per task**, created serially, message in the repo's existing style (`feat:` / `fix:` / `test:` / `refactor:`).
- **Never delete untracked files you did not create.** Excluding them from your commits is correct and sufficient.

## File structure

| Path | Responsibility |
|---|---|
| `src/AiUsageMonitor.Infrastructure/Providers/IProcessRunner.cs` | **Create.** The process seam: capture-and-exit, plus a duplex session for `app-server`. |
| `src/AiUsageMonitor.Infrastructure/Providers/DefaultProcessRunner.cs` | **Create.** The production implementation, delegating to the existing `ProcessRunner` helper. |
| `src/AiUsageMonitor.Infrastructure/Providers/ProcessRunner.cs` | **Modify.** Stays the internal helper; gains a duplex start used by `DefaultProcessRunner`. |
| `src/AiUsageMonitor.Infrastructure/Providers/ProviderVersionCache.cs` | **Create.** Version memo keyed on executable path + last-write time. |
| `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs` | **Modify.** Constructor-injected seams; version through the cache. |
| `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs` | **Modify.** Constructor-injected seams; version through the cache. |
| `tests/AiUsageMonitor.Infrastructure.Tests/Fakes/FakeProcessRunner.cs` | **Create.** Scripted process responses for both probes' tests. |
| `tests/AiUsageMonitor.Infrastructure.Tests/CodexProbeTests.cs` | **Create.** Codex adapter contract tests. |
| `tests/AiUsageMonitor.Infrastructure.Tests/ClaudeOAuthUsageProbeTests.cs` | **Create.** Claude adapter contract tests. |
| `tests/AiUsageMonitor.Infrastructure.Tests/ProviderVersionCacheTests.cs` | **Create.** Cache hit, miss, invalidation, and never-cache-null. |

---

### Task 1: The process seam

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Providers/IProcessRunner.cs`
- Create: `src/AiUsageMonitor.Infrastructure/Providers/DefaultProcessRunner.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/ProcessRunner.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/Fakes/FakeProcessRunner.cs`

**Interfaces — produces:**

```csharp
namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>One live child process, as the probes use it: write requests, read framed replies.</summary>
public interface IProcessSession : IDisposable
{
    TextWriter StandardInput { get; }
    TextReader StandardOutput { get; }
    Task WaitForExitAsync(CancellationToken ct);
}

/// <summary>
/// How a probe launches a provider executable. Exists so the two adapters can be tested without a
/// real install: production wiring uses <see cref="DefaultProcessRunner"/> and behaves exactly as
/// the static helper always did.
/// </summary>
public interface IProcessRunner
{
    Task<(int ExitCode, string StdOut, string StdErr)> RunCapturedAsync(
        string exePath, string arguments, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Starts a duplex session. The caller owns disposal, which must kill the process tree if it
    /// has not already exited.
    /// </summary>
    IProcessSession Start(string exePath, string arguments);
}

public sealed class DefaultProcessRunner : IProcessRunner
{
    public static DefaultProcessRunner Instance { get; }
}
```

**Requirements:**

- `DefaultProcessRunner.RunCapturedAsync` delegates to the existing `ProcessRunner.RunCapturedAsync` with no behaviour change — same `UseShellExecute = false`, same UTF-8-no-BOM streams, same `CancelAfter(timeout)` plus `TryKill` backstop.
- `DefaultProcessRunner.Start` creates the `app-server` shape currently built inline in `CodexProbe.ReadRateLimitsAsync`: `UseShellExecute = false`, stdin/stdout/stderr redirected, `StandardOutputEncoding`/`StandardErrorEncoding`/`StandardInputEncoding` all `new UTF8Encoding(false)`, `CreateNoWindow = true`. Move that `ProcessStartInfo` construction into `ProcessRunner` so there is one definition of how this application launches a provider executable.
- The returned session's `Dispose` calls `ProcessRunner.TryKill` and then disposes the `Process`. Disposing twice must be safe.
- `FakeProcessRunner` (test project) is scriptable per `(exePath, arguments)`: a queued `(int, string, string)` for `RunCapturedAsync`, and for `Start` a session whose `StandardOutput` replays a supplied list of lines and whose `StandardInput` records what was written. It must also be able to simulate: stdout closing immediately (no lines), and a `RunCapturedAsync` that throws `Win32Exception` or times out via `OperationCanceledException`.
- `FakeProcessRunner` must record how many times `RunCapturedAsync` was called per executable path — Task 4's cache test depends on it.

**Acceptance criteria:**

- `dotnet build` clean; `dotnet test` green with no test count regression.
- No production call site changes behaviour: `CodexProbe` and `ClaudeOAuthUsageProbe` still work against a real install through `DefaultProcessRunner.Instance`.
- `ProcessRunner` remains `internal`; the new interface and default implementation are `public`.

---

### Task 2: Codex adapter contract tests

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/CodexProbeTests.cs`

**Interfaces — consumes:** `IProcessRunner`, `IProcessSession`, `FakeProcessRunner` from Task 1.

**Interfaces — produces:**

```csharp
public sealed class CodexProbe : IProviderProbe
{
    public CodexProbe(IProcessRunner? processes = null, Func<string?>? locateExecutable = null);
}
```

`locateExecutable` defaults to `CodexExecutableLocator.Locate`. `processes` defaults to `DefaultProcessRunner.Instance`. The parameterless construction used by `ProviderRegistry` must keep compiling unchanged.

**Requirements:**

- Replace the inline `ProcessStartInfo`/`Process` use in `ReadRateLimitsAsync` with `IProcessSession`. The protocol behaviour must not change: write `initialize` (id 1) then `account/rateLimits/read` (id 2), flush once, then read lines until `CodexProtocol.TryReadResult` returns true; close stdin; wait for exit; `RateLimitsTimeout` still bounds the whole exchange via a linked `CancellationTokenSource`.
- No behaviour change to snapshot construction, `MapRateLimits`, or error text.

**Acceptance criteria — each is one test:**

1. **Absent install.** `locateExecutable` returns `null` → `State == NotInstalled`, `Installed == false`, `Windows` empty, `Error` null, and the note names the three locations checked. No process is ever started.
2. **Happy path.** A scripted session replaying a realistic `rateLimitsByLimitId` frame (use the real shape: a `primary` object with `usedPercent`, `resetsAt` as unix seconds, `windowDurationMins`, and `secondary: null`) → `State == Connected`, `Tier == Official`, one window whose `UsedPercent`, `ResetsAt` and `WindowDuration` match, `RetrievedAt` non-null, `Error` null.
3. **Interleaved notifications.** The session emits `{"method":"remoteControl/status/changed","params":{}}` and an id:1 frame *before* the id:2 result. The probe must skip both and still return `Connected` with the window. This is the documented trap that breaks naive clients — the test must prove the id:2 frame is found after non-answers, not merely that parsing works.
4. **Protocol error frame.** The session emits `{"id":2,"error":{"code":-32600,"message":"Not initialized"}}` → `State == Error`; `Error` is exactly `"The Codex app-server rejected the rate-limit request (error -32600)."`; **`Error` contains neither `"Not initialized"` nor any brace** — assert that explicitly, because this is the path that used to render `GetRawText()`.
5. **stdout closes with no id:2 frame** → `State == Error` and `Error` is the app-authored `ProviderMechanismException` message, not a framework message.
6. **Malformed JSONL.** A line of `not json at all` followed by the real id:2 frame → skipped, `Connected`.
7. **Caller cancellation.** A pre-cancelled `CancellationToken` → `OperationCanceledException` propagates out of `ProbeAsync` (the refresh service depends on telling shutdown apart from provider failure). Assert the exception, not a snapshot.
8. **Partial window.** A `primary` with `usedPercent` but no `windowDurationMins` → the window has `IsPartial == true`, `WindowDuration == null`, and `UsedPercent` still populated. Nothing becomes `0`.
9. **Version failure is survivable.** `RunCapturedAsync` throws `Win32Exception` → the rate-limit read still happens, `State == Connected`, `Version == null`.

---

### Task 3: Claude adapter contract tests

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ClaudeOAuthUsageProbeTests.cs`

**Interfaces — produces:**

```csharp
public sealed class ClaudeOAuthUsageProbe : IProviderProbe
{
    public ClaudeOAuthUsageProbe(
        IProcessRunner? processes = null,
        HttpMessageHandler? handler = null,
        Func<string?>? locateExecutable = null,
        Func<string>? credentialsPath = null);
}
```

- `locateExecutable` defaults to `ClaudeExecutableLocator.Locate`; `credentialsPath` defaults to today's `GetCredentialsPath()`; `handler` defaults to the existing shared static client. When a handler *is* supplied, build a per-instance `HttpClient` over it with the same `Timeout` and the same `AllowAutoRedirect = false` intent. The parameterless construction used by `ProviderRegistry` must keep compiling unchanged.
- The shared static `HttpClient` must not be replaced or mutated by an injected handler — tests must not be able to affect the production client.

**Acceptance criteria — each is one test. Use a stub `HttpMessageHandler` and a temp-directory credentials file; never a real network call.**

1. **Absent install.** `locateExecutable` returns `null` → `NotInstalled`, `Installed == false`, no HTTP request issued.
2. **No credentials file** → `Unavailable`, `Error` is the existing "installed but has not stored a sign-in" copy, no HTTP request issued.
3. **Malformed credentials file** (valid file, no `claudeAiOauth`) → `Unavailable`, no HTTP request issued.
4. **Happy path.** A body in the usage-endpoint dialect (`utilization` + ISO-8601 `resets_at`) → `Connected`, `Tier == Unofficial`, windows discovered by the shared extractor, `RetrievedAt` non-null.
5. **The token reaches exactly one header and nowhere else.** Write a recognisable sentinel token into the temp credentials file. Assert the outgoing request's `Authorization` header is `Bearer <sentinel>`, and then assert the sentinel appears in **no** `Note`, not in `Error`, not in any `QuotaWindow.Extra` value, and not in `Mechanism`. This is the credential constraint, expressed as a test.
6. **401 and 403** → `Error` with exactly the `TokenRejectedMessage` constant; the note records the status code only.
7. **500** → `Error`; the note lists **top-level JSON key names only**; assert a distinctive *value* from the body appears nowhere in the snapshot.
8. **Malformed success body** → `Error` with `"Response body was not valid JSON."`, no windows.
9. **`HttpRequestException`** → `Error` text comes from `ProviderErrorText.For` and is one of its app-authored strings — assert it does not contain the exception's own message.
10. **Caller cancellation** propagates as `OperationCanceledException` rather than becoming an `Error` snapshot.

---

### Task 4: Version caching

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Providers/ProviderVersionCache.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderVersionCacheTests.cs`
- Test: additions to `CodexProbeTests.cs` and `ClaudeOAuthUsageProbeTests.cs`

**Interfaces — produces:**

```csharp
namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>
/// Remembers a provider executable's reported version so a hidden widget does not relaunch it once
/// a minute for a string that changes on upgrade. Keyed on the path AND its last-write time, so an
/// upgrade in place invalidates without any version comparison.
/// </summary>
public sealed class ProviderVersionCache
{
    public bool TryGet(string exePath, DateTime lastWriteUtc, out string version);
    public void Store(string exePath, DateTime lastWriteUtc, string version);
}
```

Both probes gain an optional constructor parameter `ProviderVersionCache? versions = null` (defaulting to a private instance owned by that probe) and `Func<string, DateTime>? lastWriteUtc = null` (defaulting to `File.GetLastWriteTimeUtc`).

**Requirements:**

- **A null or unparseable version is never stored.** A `--version` that failed must be retried on the next poll; caching absence would make a transient failure permanent for the life of the process.
- The cache is thread-safe. Both probes can be in flight concurrently, and the refresh service probes providers concurrently by design.
- When the last-write lookup itself throws (`IOException`, `UnauthorizedAccessException`), fall back to launching the executable rather than failing the probe. A version is never worth a failed snapshot.
- When a version is served from cache, the probe adds the note `Version {version} (cached; executable unchanged since it was read).` and does **not** add its usual `--version` note. Notes stay truthful about what actually happened.
- Path comparison is `OrdinalIgnoreCase` (Windows).

**Acceptance criteria:**

1. `ProviderVersionCacheTests`: store then get with the same path and timestamp → hit. Same path, later timestamp → miss. Different path → miss. Path differing only in case → hit. Concurrent `Store`/`TryGet` from several tasks → no exception.
2. `CodexProbeTests`: two consecutive `ProbeAsync` calls on one probe instance, with an unchanged fake last-write time, issue **one** `--version` `RunCapturedAsync` call (assert `FakeProcessRunner`'s recorded count), and both snapshots carry the same `Version`.
3. `CodexProbeTests`: two consecutive calls with a *changed* last-write time issue **two** `--version` calls and the second snapshot carries the new version.
4. `CodexProbeTests`: a first call whose `--version` fails, then a second whose `--version` succeeds → **two** calls, and the second snapshot carries the version. Proves absence is not cached.
5. The equivalent of (2) for `ClaudeOAuthUsageProbeTests`.
6. Snapshot construction is otherwise unchanged: existing tests still pass.

---

## Out of scope — recorded, not forgotten

| Excluded | Why |
|---|---|
| A diagnostics view (C10 / X10) | Its own increment. This plan makes the probes testable; it does not surface anything new in the UI. |
| Surfacing `Notes` anywhere in the UI | `Notes` can carry a local credentials path. It belongs behind the deliberate look of a diagnostics view, not on an always-visible card. |
| Per-provider refresh intervals (C15) | Not in §2.1 or §3.1. |
| Any change to `DuckTypedQuotaExtractor` | It is already directly tested and is not the drift boundary X6 names. |
| Retiring the static `HttpClient` | The injected handler is a test seam. Changing production HTTP lifetime is a separate risk with no requirement behind it. |

## Verification

**A copy of the widget is running on this machine and holds `src/AiUsageMonitor.App/bin/…`.** A plain `dotnet build` therefore fails with `MSB3021: The process cannot access the file … because it is being used by another process`. That is an environment condition, not a defect in your work, and **it is not a reason to kill the user's process.** Redirect the output instead, and run the two commands **separately**:

```powershell
dotnet build -p:BaseOutputPath=$env:TEMP/aium-build/
dotnet test  -p:BaseOutputPath=$env:TEMP/aium-build/
```

Both must be clean — build with 0 warnings and 0 errors, tests with 0 failures. Baseline before this increment: **428 passing** (100 Domain / 138 Infrastructure / 190 App). State the after count in the final summary.

Never terminate `AiUsageMonitor.App.exe`, and never write build output inside the repository.
