# Claude Scoped-Limit Normalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Teach the Claude adapter to read the `limits[]` array in the usage response without rendering every quota window twice, giving genuinely new entries stable identities derived from the provider's own vocabulary rather than array positions.

**Architecture:** All of it lives inside the Claude infrastructure adapter. A new `ClaudeScopedLimits` static class turns `limits[]` entries into `QuotaWindow` candidates, drops any that restate a window the shared extractor already found, and appends the rest. `Domain/DuckTypedQuotaExtractor.cs` is **not** touched — `percent` must not join the shared key list.

**Tech Stack:** .NET 10, C# 13, xUnit, `System.Text.Json`. No new package references.

**Spec:** `docs/specs/2026-08-14-provider-request-cadence-and-rate-limits.md` §4.6 **and Appendix A**. Appendix A was captured from the live endpoint after §4.6 was written and materially changes what this slice must do — read it first.

## The finding that drives this plan

§4.6 assumed `limits[]` carried model-scoped quotas not otherwise present. Appendix A shows that on the observed account it carries **exact duplicates** of the two top-level windows:

| `limits[]` entry | `kind` | `group` | `scope` | `is_active` | Relationship to top level |
|---|---|---|---|---|---|
| `limits[0]` | `session` | `session` | `null` | `true` | `percent` == `five_hour.utilization`, `resets_at` identical |
| `limits[1]` | `weekly_all` | `weekly` | `null` | `false` | `percent` == `seven_day.utilization`, `resets_at` identical |

So the first job is **suppression, not surfacing**. Four consequences bind every task below:

1. Naively teaching the shared extractor about `percent` would show every window twice. That is a visible regression, not a theoretical risk.
2. `group`/`kind` use a different vocabulary from the top-level keys (`session` vs `five_hour`, `weekly` vs `seven_day`), so duplicate detection **cannot** be a name comparison. Reset instant plus percentage is the only observed reliable correspondence.
3. `is_active: false` is **not** a hide signal — `weekly_all` reports inactive while `seven_day` is live and published. Never suppress a window because a `limits[]` twin says it is inactive.
4. `scope` is null on the observed account, so a model-scoped entry is **unverified**. Write the code so a scoped entry would be handled correctly; never invent one, and never claim in a comment that scoped entries have been seen.

## Global Constraints

Copied from `CLAUDE.md` and `docs/PRD.md`. Every task's requirements implicitly include this section.

- `dotnet build` must be **clean — warnings are errors**.
- **Run build and test as separate commands, never chained.** `dotnet build`, then `dotnet test`.
- **`src/AiUsageMonitor.Domain` must not change.** No new key in `DuckTypedQuotaExtractor.PercentKeys`, no Claude-shaped field name anywhere in `Domain/`. If a task appears to need a domain change, stop and report rather than making one.
- **Missing data is `null`, never `0`.** An entry with no `percent` has unknown usage; an entry with `percent: 0` has zero usage. These are different and must stay different.
- **Never invent a reset.** A missing or unparseable `resets_at` stays null and the window is `IsPartial`.
- Never copy raw response content into `Notes`, `Extra`, `Error`, logs, or diagnostics. Key names and application-authored text only.
- Unrecognised provider tokens are preserved verbatim and never reinterpreted.

---

### Task 1: Add a synthetic fixture for the observed response shape

Nothing downstream can be tested honestly without a recorded shape, and the live response contains the author's real usage figures.

**Files:**
- Create: `fixtures/claude-usage-limits-sample.json`
- Modify: `CLAUDE.md` (one line in the Claude Code section noting what the new fixture is for)

**Requirements:**

1. The fixture mirrors the **structure** in Appendix A exactly — same keys, same nesting, same null-vs-present pattern — with **synthetic values**. Real percentages and reset instants are personal usage data and must not be committed. Say so in the accompanying `CLAUDE.md` line.
2. It must exercise every case the later tasks need, so give it more than the two entries the live account returned:

```json
{
  "five_hour": {
    "utilization": 41.5,
    "resets_at": "2026-08-17T20:50:00.000000+00:00",
    "limit_dollars": null, "used_dollars": null, "remaining_dollars": null
  },
  "seven_day": {
    "utilization": 12.25,
    "resets_at": "2026-08-24T15:00:00.000000+00:00",
    "limit_dollars": null, "used_dollars": null, "remaining_dollars": null
  },
  "seven_day_opus": null,
  "nimbus_quill": {
    "utilization": 3.0,
    "resets_at": null,
    "limit_dollars": null, "used_dollars": null, "remaining_dollars": null
  },
  "extra_usage": {
    "is_enabled": false, "monthly_limit": null, "used_credits": 0,
    "utilization": null, "currency": "USD", "decimal_places": 2,
    "disabled_reason": "not_enabled", "user_disabled": false,
    "spend_limit_reached": false, "credits_ever_enabled": false,
    "daily": null, "weekly": null
  },
  "limits": [
    {
      "kind": "session", "group": "session", "percent": 41.5,
      "severity": "normal", "resets_at": "2026-08-17T20:50:00.000000+00:00",
      "scope": null, "is_active": true
    },
    {
      "kind": "weekly_all", "group": "weekly", "percent": 12.25,
      "severity": "normal", "resets_at": "2026-08-24T15:00:00.000000+00:00",
      "scope": null, "is_active": false
    },
    {
      "kind": "weekly_scoped", "group": "weekly", "percent": 66.0,
      "severity": "warning", "resets_at": "2026-08-24T15:00:00.000000+00:00",
      "scope": "opus", "is_active": true
    },
    {
      "kind": "weekly_scoped", "group": "weekly", "percent": 0,
      "severity": "normal", "resets_at": "2026-08-24T15:00:00.000000+00:00",
      "scope": "sonnet", "is_active": true
    },
    {
      "kind": "monthly_scoped", "group": "monthly", "percent": 8.0,
      "severity": "normal", "resets_at": null,
      "scope": "haiku", "is_active": true
    },
    {
      "kind": "", "group": "", "percent": 5.0,
      "severity": "normal", "resets_at": "2026-08-24T15:00:00.000000+00:00",
      "scope": null, "is_active": true
    },
    {
      "kind": "no_percent", "group": "weekly",
      "severity": "normal", "resets_at": "2026-08-24T15:00:00.000000+00:00",
      "scope": null, "is_active": true
    }
  ],
  "spend": {
    "used": { "amount_minor": 0, "currency": "USD", "exponent": 2 },
    "limit": null, "percent": 0.0, "severity": "normal", "enabled": false,
    "disabled_reason": "not_enabled", "cap": null, "balance": null,
    "auto_reload": null, "disclaimer": "synthetic", "can_purchase_credits": false,
    "can_toggle": false
  },
  "member_dashboard_available": false
}
```

   The seven `limits[]` entries cover, in order: a duplicate of `five_hour`; a duplicate of `seven_day` that is also `is_active: false`; a genuinely new scoped entry; a scoped entry with an explicit zero; a scoped entry with a missing reset; an entry with no usable identity; and an entry with no percentage.

3. Ensure the fixture is copied to the test output. Check how `fixtures/claude-statusline-sample.json` is wired into `tests/AiUsageMonitor.Infrastructure.Tests` (or `Domain.Tests`) and follow the same mechanism; do not invent a second one.

**Verification:** `dotnet build`, then `dotnet test` — still green, nothing consumes the fixture yet.

**Commit:** `test: add a synthetic fixture for the Claude usage limits array`

---

### Task 2: Normalize `limits[]` into candidate quota windows

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeScopedLimits.cs`
- Test: create `tests/AiUsageMonitor.Infrastructure.Tests/ClaudeScopedLimitsTests.cs`

**Interfaces:**
- Produces: `internal static class ClaudeScopedLimits` with
  `public static IReadOnlyList<QuotaWindow> Normalize(JsonElement root, IReadOnlyList<QuotaWindow> alreadyFound)`.
  Make the class `public` only if the test project cannot see internals; check for an existing `InternalsVisibleTo` before deciding.

**Requirements:**

1. **Identity comes from the provider's vocabulary, never from array position.** `limits[2]` is not an identity — it changes the moment the provider reorders or adds an entry, which would silently rename a window between two refreshes.

```csharp
/// <summary>
/// A stable window id built from the entry's own declared identity: its kind, plus the model scope
/// when the entry is scoped to one. Array position is deliberately not part of it - "limits[2]"
/// changes the moment the provider reorders the array, which would silently rename a window
/// between two refreshes of the same account.
/// </summary>
private static string? IdFor(string? kind, string? scope)
{
    if (string.IsNullOrWhiteSpace(kind))
    {
        return null;   // no declared identity -> not stable enough to surface at all
    }

    return string.IsNullOrWhiteSpace(scope) ? kind.Trim() : $"{kind.Trim()}_{scope.Trim()}";
}
```

2. An entry is **skipped entirely** when: it is not a JSON object; `IdFor` returns null; or it has no `percent` property of kind `Number`. A missing percentage is unknown usage, and a window whose usage is unknown and whose identity is only half-declared is noise, not information.
3. An entry with `percent: 0` **is surfaced**, with `UsedPercent = 0`. Explicit zero is a fact.
4. `resets_at` is parsed as ISO-8601. Absent, null, or unparseable ⇒ `ResetsAt = null` and `IsPartial = true`. Never substitute a reset.
5. `is_active` is recorded, never acted on. Put it in `Extra` as `"claude.is_active"` = `"true"`/`"false"`. Appendix A point 3 is the reason: the live account reports `weekly_all` inactive while `seven_day` is live and published.
6. Labels go through the shared humaniser so unrecognised tokens survive verbatim:

```csharp
bool labelIsProviderToken = !DuckTypedQuotaExtractor.TryHumanize(id, out string label);
```

   `session` and `weekly_all` do not parse as "<number> <unit>", so they keep their raw token and `LabelIsProviderToken` is true — which is exactly what the UI needs to render them distinctly.
7. `WindowDuration` is left null unless `TryHumanize` succeeded and the shared extractor's own inference produced one; do not add a Claude-specific duration table. A null duration simply omits the elapsed marker.
8. `Extra` carries only application-authored keys and the entry's own low-cardinality identity tokens — `claude.kind`, `claude.group`, `claude.scope` (omitted when null), `claude.is_active`, and `claude.source` = `"limits"`. **Never** `severity` interpreted as anything, and never a raw fragment of the response.
9. `Order` continues the sequence: candidates are numbered from `alreadyFound.Count` upward, in array order, so ordering is stable across refreshes.
10. The method must not mutate `alreadyFound` — it returns a new list containing only the candidates.

**Tests to add** (`ClaudeScopedLimitsTests`, driving the Task 1 fixture):

| Test | Asserts |
|---|---|
| `AnEntryWithKindAndScopeGetsACompositeId` | The `weekly_scoped` + `opus` entry produces `Id == "weekly_scoped_opus"`. |
| `AnEntryWithNoScopeUsesItsKindAlone` | Produces `Id == "session"`, not `"limits[0]"`. |
| `NoProducedIdContainsAnArrayIndex` | No candidate `Id` matches `limits[`. |
| `AnEntryWithNoKindIsSkipped` | The `kind: ""` entry produces no candidate. |
| `AnEntryWithNoPercentIsSkipped` | The `no_percent` entry produces no candidate. |
| `AnExplicitZeroPercentIsSurfacedAsZero` | The `sonnet` entry has `UsedPercent == 0`, **not** null and not skipped. |
| `AMissingResetStaysMissing` | The `haiku` entry has `ResetsAt == null` and `IsPartial == true`. |
| `AnInactiveEntryIsStillNormalized` | The `weekly_all` entry produces a candidate carrying `Extra["claude.is_active"] == "false"`. |
| `AnUnrecognisedKindKeepsItsRawToken` | `session` ⇒ `Label == "session"` and `LabelIsProviderToken == true`. |
| `ExtraNeverCarriesRawResponseContent` | No `Extra` value contains `"severity"`'s value, `"disclaimer"`, or any string not in the allowed key set. |
| `OrderContinuesFromTheWindowsAlreadyFound` | With `alreadyFound.Count == 3`, the first candidate has `Order == 3`. |
| `NormalizeDoesNotMutateItsInput` | `alreadyFound` is unchanged by reference and by content. |
| `AMissingOrNonArrayLimitsPropertyYieldsNoCandidates` | `{}` and `{"limits": 5}` both produce an empty list without throwing. |

**Verification:** `dotnet build`, then `dotnet test`.

**Commit:** `feat: normalize the Claude limits array into stable quota window candidates`

---

### Task 3: Suppress candidates that restate a window already found

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeScopedLimits.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ClaudeScopedLimitsTests.cs`

**Requirements:**

1. A candidate is a duplicate of an already-found window when **both** hold:
   - both have a non-null `ResetsAt` and they are equal **to the second** (`ResetsAt.Value.ToUnixTimeSeconds()`), and
   - both have a non-null `UsedPercent` and they agree within a small epsilon (`Math.Abs(a - b) < 0.0001`).

```csharp
/// <summary>
/// Whether this candidate is the same quota the shared extractor already found under a top-level
/// key. Matching is on reset instant plus percentage, NOT on name: the array uses a different
/// vocabulary from the top-level keys - "session" for "five_hour", "weekly" for "seven_day" - so a
/// name comparison would miss every real duplicate. A candidate with an unknown reset or unknown
/// usage can never be proven to be a duplicate and is therefore kept.
/// </summary>
private static bool DuplicatesAnExistingWindow(QuotaWindow candidate, IReadOnlyList<QuotaWindow> alreadyFound)
{
    if (candidate.ResetsAt is not DateTimeOffset candidateReset || candidate.UsedPercent is not double candidatePercent)
    {
        return false;
    }

    foreach (QuotaWindow existing in alreadyFound)
    {
        if (existing.ResetsAt is DateTimeOffset existingReset
            && existing.UsedPercent is double existingPercent
            && existingReset.ToUnixTimeSeconds() == candidateReset.ToUnixTimeSeconds()
            && Math.Abs(existingPercent - candidatePercent) < 0.0001)
        {
            return true;
        }
    }

    return false;
}
```

2. **Never suppress an already-found window.** Suppression only ever removes a candidate. The top-level windows are the established, tested representation; `limits[]` is the newcomer and must yield to them.
3. Two candidates that duplicate **each other** but not an existing window are both kept if their ids differ — different ids mean the provider is asserting they are different quotas, and this code does not overrule that.
4. Wire suppression into `Normalize` so it returns only the survivors, renumbering `Order` so the sequence has no gaps.

**Tests to add:**

| Test | Asserts |
|---|---|
| `ACandidateMatchingATopLevelWindowIsSuppressed` | Given the fixture's `five_hour` window as `alreadyFound`, the `session` candidate is absent. |
| `ADuplicateIsSuppressedDespiteADifferentName` | `session` vs `five_hour` — proves matching is not by name. |
| `AnInactiveDuplicateIsAlsoSuppressed` | The `weekly_all` candidate is absent when `seven_day` is already found. |
| `AGenuinelyNewScopedCandidateSurvives` | `weekly_scoped_opus` survives — same reset as `seven_day` but a different percentage. |
| `ACandidateWithAnUnknownResetIsNeverTreatedAsADuplicate` | The `haiku` candidate survives. |
| `SuppressionNeverRemovesAnAlreadyFoundWindow` | `alreadyFound` still has its original count and contents after `Normalize`. |
| `SurvivorsAreNumberedContiguouslyAfterTheExistingWindows` | With three existing windows and two survivors, `Order` values are 3 and 4. |
| `TheOrderOfSurvivorsFollowsTheArrayOrder` | Two runs over the same fixture produce the same ids in the same order. |

**Verification:** `dotnet build`, then `dotnet test`.

**Commit:** `feat: suppress limits entries that restate a top-level quota window`

---

### Task 4: Wire normalization into the Claude probe

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs` (the success branch, around the `DuckTypedQuotaExtractor.Extract` call)
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ClaudeOAuthUsageProbeTests.cs`

**Requirements:**

1. In the success branch, extract as today, then defensively drop any array-positional window before appending candidates:

```csharp
IReadOnlyList<QuotaWindow> extracted = DuckTypedQuotaExtractor.Extract(doc.RootElement);

// Defensive, and cheap. The shared extractor does not currently produce a window from a limits[]
// entry - the entries use "percent", which is not one of its percent keys - but if the provider
// ever renames that field to one the extractor does know, it would start emitting windows whose
// ids are array positions. An id like "limits[2]" is not a provider identity: it moves whenever
// the array is reordered. Dropping them here keeps the normalized entries below the single source
// of truth for this array.
IReadOnlyList<QuotaWindow> topLevel = extracted
    .Where(w => !w.Id.StartsWith("limits[", StringComparison.Ordinal))
    .ToList();

IReadOnlyList<QuotaWindow> scoped = ClaudeScopedLimits.Normalize(doc.RootElement, topLevel);
IReadOnlyList<QuotaWindow> windows = [.. topLevel, .. scoped];
```

2. The existing notes stay, computed over the combined `windows` list. Add one application-authored note:

```csharp
notes.Add(scoped.Count == 0
    ? "No additional quota windows found in the limits array beyond those already reported."
    : $"{scoped.Count} additional quota window(s) normalized from the limits array.");
```

3. Nothing else in the probe changes. `Windows` remains the only place these appear; no new snapshot field.

**Tests to add:**

| Test | Asserts |
|---|---|
| `TheLiveShapedFixtureProducesNoDuplicateWindows` | Serving the Task 1 fixture as a 200 body, no two windows share an `Id`, and no window's `(ResetsAt, UsedPercent)` pair appears twice. |
| `TheTopLevelWindowsSurviveUnchanged` | `five_hour` and `seven_day` are present with their original ids, percentages and resets. |
| `AScopedLimitAppearsAsItsOwnWindow` | `weekly_scoped_opus` is present exactly once. |
| `NimbusQuillIsStillAPartialWindow` | The provider-invented top-level key still yields one window with `ResetsAt == null` and its raw label — guards the existing "preserve unrecognised tokens" rule against this change. |
| `SpendAndExtraUsageStillProduceNoWindows` | Neither `root.spend` nor `root.extra_usage` yields a window (`spend` has `percent` but no reset key; `extra_usage` has a null `utilization`). |
| `ContextWindowUsedPercentageIsStillExcluded` | Keep the existing `DuckTypedQuotaExtractorTests` assertion green — context fill is not subscription quota. |
| `TheAddedNoteNeverQuotesResponseContent` | The new note contains only a count and application-authored words. |

**Verification:** `dotnet build`, then `dotnet test`.

**Commit:** `feat: surface genuinely new Claude scoped limits without duplicating windows`

---

## Acceptance criteria mapping

| § 7 | Criterion | Task |
|---:|---|---|
| 17 | Active model-scoped limits receive stable IDs and are not duplicated by equivalent top-level windows | 2, 3, 4 |
| 18 | Missing scoped-limit usage or reset values are preserved as missing rather than invented | 2 |
| 13 | Existing retained-row, freshness and provider-isolation behaviour remains intact | 4 |
