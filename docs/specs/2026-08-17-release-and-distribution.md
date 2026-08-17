# Release and distribution

**Date:** 2026-08-17
**Status:** Decided — implemented by `docs/plans/2026-08-17-release-and-distribution.md`
**Closes:** PRD §27 (Definition of Done) items C17 in `docs/specs/2026-08-13-feature-inventory-and-ideas.md` §4.5

---

## 1. Why this increment exists

Every functional requirement in the PRD is now implemented. What is missing is
distribution: **there is no way for anyone to obtain this application.** The
repository has no `README.md`, no `LICENSE`, no version of its own, no tag, no
CI, no release, and it is private.

PRD §27 lists two Definition-of-Done items that are still open:

> - Repository documentation explains setup, supported capabilities, limitations, diagnostics, and privacy behavior.
> - Manual verification succeeds against supported installed provider versions.

and the project's standing constraint (PRD §4.1.1 addendum, added 2026-08-11):

> Someone who is not the author, on a Windows machine that is not the author's,
> must be able to download the GitHub release artifact and run it.

That constraint is currently **unsatisfiable**, because the artifact does not
exist and the repository that would host it is not readable by anyone else.

## 2. What is already in place

Verified 2026-08-17 against `244683f`:

| Fact | State |
|---|---|
| `build/publish.ps1` | ✅ Produces one self-contained ~65 MB `.exe`, and **throws** below 50 MB to catch a dropped `--self-contained` |
| Application version rendering | ✅ Diagnostics → *Application* → "Application version", via `EnvironmentReport.CaptureApplicationVersion()` |
| Version value | ❌ No `<Version>` anywhere, so the SDK default `1.0.0` is what ships and what that field displays |
| Diagnostics bundle | ✅ Redacted copy-to-clipboard, `DiagnosticRedaction.Redact` masks the user folder and user name |
| `README.md` | ❌ Does not exist |
| `LICENSE` | ❌ Does not exist — a repository with no licence grants no rights, even when public |
| `.github/` | ❌ No CI of any kind |
| Git tags | ❌ None |
| Repository visibility | ⚠️ **Private** |
| `global.json` | ❌ Absent; four SDKs are installed locally (7/8/9/10) and nothing pins the choice |

## 3. Decisions

### D1 — First release is `0.1.0`, not `1.0.0`

**Decision:** the version is `0.1.0`.

**Why:** a `1.0.0` is a stability promise. This application's Claude Code
mechanism is **unofficial and undocumented** and is expected to break without
notice (CLAUDE.md, PRD §4.1.1). A major version implies a compatibility
guarantee that depends on an endpoint the project does not control and cannot
support. `0.x` states the actual situation. This is not false modesty about the
code — it is accuracy about the mechanism.

### D2 — The version is declared once, in `Directory.Build.props`

**Decision:** `<VersionPrefix>` lives in `Directory.Build.props` and applies to
every project. CI **verifies** that a release tag matches it; CI never
**injects** a different value.

**Why:** injecting the version from the tag means a checkout cannot tell you
what version it is — the source and the artifact are only equal by coincidence
of the build. Declaring it in the tree and failing the release when the tag
disagrees makes the mismatch a build error instead of a support mystery. The
diagnostics bundle a user pastes into a bug report then provably names the
release they downloaded.

**Rejected:** MinVer, Nerdbank.GitVersioning, or any tag-derived scheme. Both
add a `PackageReference` for a problem one property solves, and CLAUDE.md
requires dependencies be minimal and justified.

### D3 — The version needs no new UI

**Decision:** Diagnostics → *Application* → "Application version" is the only
place the application's own version is shown. No About page, no title-bar
version, no tray tooltip version.

**Why:** it is already implemented, already in the redacted bundle, and already
where someone filing a bug report will look. A widget 360 px wide has no room
to spend on a number the user never needs while it is working.

### D4 — Sign nothing; document SmartScreen instead

**Decision:** the artifact is unsigned. The README states plainly that Windows
SmartScreen will warn on first run, why, and exactly what the user must click.

**Why:** an Authenticode certificate costs money annually and requires an
identity the project does not have. The alternative to documenting the warning
is a user meeting it with no explanation, which is worse — an unexplained
SmartScreen prompt on an unsigned binary is indistinguishable from malware
behaviour, and teaching users to click through warnings silently is bad
practice. Saying "this is unsigned, here is what you will see, here is the
checksum to verify" is the honest form.

**Consequence:** each release publishes a `SHA256` checksum alongside the `.exe`
so a user can verify what they downloaded is what CI built.

### D5 — Two workflows: `ci.yml` and `release.yml`

**Decision:**

- `ci.yml` — on push to `main` and on pull requests. Restore, build, test.
- `release.yml` — on a `v*` tag. Verify the tag against `VersionPrefix`, build,
  test, publish, checksum, create the GitHub release with generated notes.

**Why two:** the release job must not run on every push, and the CI job must not
be able to publish. Splitting them makes "what can create a release" a
one-file answer.

**Why `windows-latest`:** WPF, `net10.0-windows`, and `RuntimeIdentifier=win-x64`
make a Linux runner impossible. This is not a preference.

### D6 — The release job never bypasses the tests

**Decision:** `release.yml` runs `dotnet test` before it publishes, and the
tag/version check is the **first** step, before any build work.

**Why:** ordering the check first means a mistyped tag fails in seconds rather
than after a multi-minute self-contained publish. Running tests in the release
job — rather than trusting that CI already passed on that commit — closes the
window where a tag is pushed to a commit CI never ran on.

### D7 — The artifact is renamed and checksummed at release time

**Decision:** `AiUsageMonitor.App.exe` is published, then uploaded as
`QuotaMonitor-v<version>-win-x64.exe` with a matching `.sha256` file.

**Why:** the assembly is named after the solution; the product is called
**Quota Monitor** everywhere the user can see it (`TrayIcon`, the diagnostics
bundle header). A downloaded file called `AiUsageMonitor.App.exe` sitting in a
Downloads folder is not recognisably the thing the user installed. The version
in the filename means two downloads never collide.

**Not renamed:** the assembly, the solution, the namespaces, or the repository.
Renaming those is churn with no user-visible benefit.

### D8 — MIT licence

**Decision:** MIT, copyright the repository owner.

**Why:** the permissive default for a personal desktop utility; it is the licence
a user encountering an unsigned binary is least surprised by, and it imposes no
obligation on the author. **This is the one decision in this document the owner
should actively confirm rather than accept by default** — it is a legal grant,
not a technical trade-off, and it is effectively irreversible for any copy
already distributed.

### D9 — `global.json` pins the SDK feature band

**Decision:** add `global.json` pinning `10.0.301` with
`rollForward: latestFeature`.

**Why:** four SDKs are installed on the author's machine (7.0.202, 8.0.403,
9.0.311, 10.0.301) and nothing states which one this repository expects. On a
CI runner the same ambiguity is resolved by whatever the image happens to
carry. `latestFeature` accepts newer 10.0.x patches without demanding an exact
match that would break on every SDK update.

### D10 — Publication happens *after* this increment, not before it

**Decision:** making the repository public is **not** part of the implementation
work, and should not happen first. The plan delivers everything that must be
true before it, plus an explicit audit task; flipping visibility is a separate,
owner-performed action taken once those land.

**The owner offered to publish immediately.** The recommendation is to wait for
Tasks 1–3 and 7, which is hours of work, not weeks. Publishing first opens a
window in which the repository is readable by anyone while it has **no
`LICENSE`** — which grants no rights at all, so nobody may legally use what they
can see — **no `README`**, so the unofficial Claude mechanism and the privacy
boundaries are undocumented at exactly the moment strangers can read the code,
and **no release**, so there is nothing for a visitor to actually do. There is
no benefit to being early, and the audit below is cheapest to act on before the
history is out rather than after.

**Why:** publication is irreversible in the way that matters — the entire git
history becomes readable, and anything ever committed stays in a clone whatever
is deleted afterwards. Two specific things must be looked at first:

1. **`fixtures/claude-statusline-sample.json` contains captured account data** —
   the author's real quota percentages and a session cost at a point in time.
   Its `session_id` is already redacted. This is low sensitivity, not zero, and
   the choice to publish it should be deliberate.
2. **No credential has ever been committed.** The application is built so that a
   token cannot reach a fixture, a log, or a diagnostic bundle, but that is an
   argument about the current code, not a statement about every commit in the
   history. It must be checked, not assumed.

Until the repository is public, a release exists but is downloadable only by the
owner — the standing user-agnostic constraint is *prepared for* by this
increment and *satisfied* by the publication that follows it.

## 4. Deliberate omissions

| Omitted | Why |
|---|---|
| `CHANGELOG.md` | GitHub's generated release notes read the commit log, which is already conventional-commit formatted. A hand-maintained changelog would restate it and drift. |
| Screenshots in the README | No captured asset exists, and a screenshot of the author's widget shows the author's real quota percentages. Add one only after deciding that is acceptable. |
| An installer (MSI/MSIX) | A single self-contained `.exe` with no admin requirement is already the lowest-friction install. An installer adds a signing problem (D4) and a per-machine footprint the app deliberately does not have. |
| `winget` / Microsoft Store | Both require a signed, identity-verified publisher. Blocked by D4, not by effort. |
| ARM64 build | Windows on ARM runs x64 under emulation. A second RID doubles the release surface for an unmeasured audience. |
| Auto-update | PRD §28 lists update discovery as a future enhancement; it needs a release feed to discover, which does not exist until this increment ships. |
| A `dependabot.yml` | Two `PackageReference`s, both Microsoft-first-party. Not worth the noise. |

## 5. Acceptance

1. `Directory.Build.props` declares `0.1.0`; Diagnostics → *Application* shows `0.1.0`.
2. `global.json` pins the SDK; `dotnet --version` in the repo root reports a 10.0.3xx.
3. `README.md` covers: what it is, install, SmartScreen, the two providers **with their tiers**, what the app never does, settings, diagnostics, limitations, and building from source.
4. `LICENSE` exists and GitHub recognises it.
5. `build/publish.ps1` emits `QuotaMonitor-v0.1.0-win-x64.exe` and a `.sha256`, and still throws below 50 MB.
6. `ci.yml` is green on `main`.
7. `release.yml` refuses a tag that disagrees with `VersionPrefix`, before building.
8. The pre-publication audit is written down, with a finding for each of the two items in D10.
9. No new `PackageReference` in any project.
10. `dotnet build` clean, warnings as errors; `dotnet test` green.
