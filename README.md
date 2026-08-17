# Quota Monitor

A Windows desktop widget that shows live quota usage for the AI coding tools installed on your machine — Claude Code and Codex — so you can see how much of your plan's window you have left without opening a session and asking.

> The repository is called `ai-agent-usage-monitor`; the application calls itself **Quota Monitor**. Same thing.

**Pre-1.0.** Windows 11, x64. No installer, no administrator rights, nothing written outside your own user profile.

---

## Providers

| Provider | What it reads | Tier | Verified against |
|---|---|---|---|
| **Codex** | `codex app-server` over stdio, JSON-RPC `account/rateLimits/read` | **Official** | codex-cli 0.144.6 |
| **Claude Code** | Anthropic's own usage endpoint, authenticated with the OAuth token Claude Code already stored on this machine | **Unofficial** | Claude Code 2.1.226 |

**The Claude Code mechanism is undocumented and may stop working without notice.** It is not a published API and carries no stability guarantee. The application labels it *Unofficial* everywhere it appears, and when it breaks the provider card goes to an explicit error state — it never falls back to a stale number, an estimate, or a zero. If you see a number, a provider returned it.

A provider that is not installed shows as **Not installed** and costs nothing. You do not need both.

## Install

1. Download `QuotaMonitor-v<version>-win-x64.exe` from the [latest release](../../releases/latest).
2. Run it. There is no installer and no setup step.

That single file contains its own .NET runtime, so nothing else needs installing.

**If your network blocks `.exe` downloads**, take `QuotaMonitor-v<version>-win-x64.zip` from the same release instead. It holds the identical executable plus its checksum file, built in the same release run. Extract both, then continue below. If your filter inspects inside archives, it will block this too — that is the filter working as intended, and the answer is your IT department, not a workaround.

### Windows will warn you, and here is why

The executable is **not code-signed**, so on first run Windows SmartScreen shows **"Windows protected your PC"**. Click **More info**, then **Run anyway**. This is the same whether you downloaded the `.exe` or extracted it from the `.zip` — Windows carries the download mark through extraction, and the zip is not a way around the warning.

A signing certificate costs money annually and requires a verified publisher identity this project does not have. Rather than leave you to guess, here is what to check instead — every release publishes a SHA256 next to the binary:

```powershell
Get-FileHash .\QuotaMonitor-v<version>-win-x64.exe -Algorithm SHA256
```

Compare the result against the contents of the matching `.sha256` file — on the release page if you downloaded the `.exe` directly, or the copy extracted from the `.zip`. If they differ, do not run it.

### Where it puts things

| Path | Contents |
|---|---|
| `%APPDATA%\AiUsageMonitor\settings.json` | Your settings. Plain JSON, hand-editable. |
| `%LOCALAPPDATA%\AiUsageMonitor\logs` | Rolling log, 1 MB × 5 files. |
| `HKCU\...\Run` | One entry, and **only** if you switch on *Start with Windows*. |

Nothing is written machine-wide. To uninstall, switch off *Start with Windows*, delete the `.exe`, and delete those two folders.

## Using it

- **The widget** shows one card per provider: name, version, mechanism tier, connection state, and a bar per quota window with its usage and a countdown to reset.
- **Closing hides it.** The close button, Alt+F4 and the system menu all hide the widget to the notification area. Only **Exit** in the tray menu ends the process.
- **The tray icon is a live readout** — a miniature of the bars, redrawn as the state changes, coloured to stay legible against your taskbar rather than against the app's theme.
- **`Ctrl+Alt+Q`** brings the widget back from anywhere. Can be switched off.
- **Pin** keeps it above other windows; unpinned, it hides when you click away.
- **Compact** tightens everything; **Mini** shrinks it to a docked strip.
- **Notifications** fire as you cross usage milestones, and when a provider fails or recovers. An alert never says *why* a provider failed — that stays on the card.

## Settings

Everything applies immediately; there is no OK or Apply.

| | |
|---|---|
| **Appearance** | Theme (Light / Dark / High contrast / follow Windows), density, colour bars by usage |
| **Window** | Always on top, mini mode and its dock edge, global hotkey, start with Windows, reset window position |
| **Providers** | Order, visibility, and a per-provider refresh interval |
| **Refresh** | Refresh interval (default 60 s) and the age at which data is called stale (default 5 min) |
| **Notifications** | On or off, which usage thresholds alert, and quiet hours |
| **Diagnostics** | Everything the app knows about each provider, plus a redacted copy button |

**The refresh interval will not go below 60 seconds**, globally or per provider. Asking a provider for your quota far more often than it changes is how an application gets throttled, and a throttled provider tells you *less*, not more. A hand-edited smaller value in `settings.json` is read as 60 and left alone in the file.

## Diagnostics, and reporting a problem

**Settings → Diagnostics → Copy** puts a plain-text report on your clipboard: what each provider reported, when, how long it took, what is scheduled next, and why. Your user folder and user name are replaced with `%USERPROFILE%` and `%USERNAME%` before it is copied. No credential is read into that screen at all, so none can leak through it.

Please include that report when opening an issue.

## Privacy

What this application does over the network is exactly two things: it asks Codex's local `app-server` for your rate limits, and it makes one HTTPS request to `api.anthropic.com` for Claude Code's. Nothing else.

It does **not**:

- scrape any website, drive a browser, or read browser cookies or profiles;
- send telemetry, analytics, crash reports, or anything at all to the author or any third party;
- follow redirects — the one permitted destination is hardcoded;
- write, refresh, rotate or modify your credentials; token lifecycle stays your provider's job;
- modify Claude Code's or Codex's own configuration files;
- require administrator rights, or install a service, driver or scheduled task.

The Claude Code OAuth token is read from `%USERPROFILE%\.claude\.credentials.json` into memory, used once to build one `Authorization` header, and never logged, persisted, cached, displayed, copied, or placed in any diagnostic output.

**If you think you have found a way to break any of that**, see [SECURITY.md](SECURITY.md) and report it privately — never as a public issue.

## Limitations

- **Windows only.** WPF and Windows-specific APIs throughout; there is no macOS or Linux build.
- **x64.** It runs under emulation on Windows on ARM; there is no native ARM64 build.
- **No history.** Every value is instantaneous. Nothing is recorded, charted or retained between runs.
- **The Claude Code mechanism is unofficial** and can break whenever the endpoint changes.
- **Quota windows are whatever the provider reports.** Their number, names and durations are discovered at runtime, not hardcoded — so a provider adding a new window shows up without an update, and one whose name the app does not recognise is displayed with the provider's own raw label rather than a guess.
- Reset times and window durations are shown only where the provider actually reports them. Where it does not, the app says so instead of inferring.

## Building from source

Requires the .NET 10 SDK (the feature band is pinned in `global.json`).

```powershell
dotnet build                                   # warnings are errors
dotnet test                                    # 725 tests
dotnet run --project src/AiUsageMonitor.Poc    # console probe against your real installs
powershell -File build/publish.ps1             # the single-file release artifact
```

`build/publish.ps1` produces the self-contained executable and stages it with its checksum under `artifacts/`. The release artifact is **always** the self-contained build — the framework-dependent one is a few hundred kilobytes, runs fine on a machine that already has the .NET 10 Desktop Runtime, and fails everywhere else.

`docs/PRD.md` is the authoritative specification. `CLAUDE.md` documents the provider mechanisms and the architectural rules in detail.

## Licence

[MIT](LICENSE).
